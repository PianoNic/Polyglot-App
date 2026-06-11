using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NSubstitute;
using Polyglot.Application.Command;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Tests;

public class SendMessageCommandTests
{
    [Test]
    public async Task Handle_NotEnoughCredits_EmitsError()
    {
        // Arrange: user has 5 credits, but sending costs ~100
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 5);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);

        var handler = CreateHandler(db, user.Id, creditsService);

        // Act
        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        // Assert
        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("Insufficient credits");
    }

    [Test]
    public async Task Handle_NotEnoughCredits_ErrorShowsAmounts()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 50);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(200L);

        var handler = CreateHandler(db, user.Id, creditsService);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        var error = ((ChatStreamError)events[0]).Message;
        await Assert.That(error).Contains("200");
        await Assert.That(error).Contains("50");
    }

    [Test]
    public async Task Handle_NotEnoughCredits_NeverCallsAI()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 0);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        var chatClient = Substitute.For<IChatClient>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        var handler = CreateHandler(db, user.Id, creditsService, factory);

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        chatClient.DidNotReceive()
            .GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_ExactlyEnoughCredits_Succeeds()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 100);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(80L);

        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient("Hi!"));

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
    }

    [Test]
    public async Task Handle_Success_EmitsChunksThenDone()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(50L);

        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient("Hello", " world"));

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hi", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events[0]).IsTypeOf<ChatStreamChunk>();
        await Assert.That(((ChatStreamChunk)events[0]).Text).IsEqualTo("Hello");
        await Assert.That(events[1]).IsTypeOf<ChatStreamChunk>();
        await Assert.That(((ChatStreamChunk)events[1]).Text).IsEqualTo(" world");
        await Assert.That(events[2]).IsTypeOf<ChatStreamDone>();
        await Assert.That(((ChatStreamDone)events[2]).Result.AssistantMessage.Content).IsEqualTo("Hello world");
    }

    [Test]
    public async Task Handle_Success_DeductsActualCostNotEstimate()
    {
        // Arrange: estimate is 500, but actual cost is 120
        var dbName = Guid.NewGuid().ToString();
        var db = CreateDb(dbName);
        var user = await SeedUserWithCredits(db, credits: 1000);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(500L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(120L);

        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient("Response"));

        // Act
        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        // Assert: 1000 - 120 = 880 (not 1000 - 500)
        var checkDb = CreateDb(dbName);
        var updatedUser = await checkDb.Users.SingleAsync(u => u.Id == user.Id);
        await Assert.That(updatedUser.CreditBalance).IsEqualTo(880);
    }

    [Test]
    public async Task Handle_Success_UsesActualTokensFromAIResponse()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(500L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);

        // AI stream reports 250 prompt tokens, 75 completion tokens
        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient(promptTokens: 250, completionTokens: 75, chunks: ["Hi"]));

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        // Should calculate cost with actual tokens (250, 75) and model prices (3, 15)
        await creditsService.Received(1).CalculateChatCreditsAsync(
            250, 75, 3m, 15m, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_Success_UsesModelPricesForEstimate()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(50L);

        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient("Hi"));

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        // Model has PromptPrice=3, CompletionPrice=15
        await creditsService.Received(1).EstimateChatCreditsAsync(
            Arg.Any<int>(), 3m, 15m, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_FreeModel_ZeroCreditsSucceeds()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 0);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(0L);
        creditsService
            .CalculateChatCreditsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(0L);

        var handler = CreateHandler(db, user.Id, creditsService, FakeStreamingClient("Free response"));

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
    }

    [Test]
    public async Task Handle_LockedUser_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        user.IsLocked = true;
        await db.SaveChangesAsync();
        await SeedModel(db);
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("locked");
    }

    [Test]
    public async Task Handle_UnknownModel_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "nonexistent"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("not found");
    }

    [Test]
    public async Task Handle_NonexistentChat_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(Guid.NewGuid(), "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("Chat not found");
    }

    [Test]
    public async Task Handle_WhitelistMode_ModelNotWhitelisted_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var settings = await db.AdminSettings.SingleAsync();
        settings.ActiveModelListMode = ModelListMode.Whitelist;
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("not available");
    }

    [Test]
    public async Task Handle_WhitelistMode_ModelWhitelisted_Succeeds()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var settings = await db.AdminSettings.SingleAsync();
        settings.ActiveModelListMode = ModelListMode.Whitelist;
        db.ModelListEntries.Add(new ModelListEntry { ModelId = "gpt-4", ListType = ModelListType.Whitelist });
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user.Id, chatClientFactory: FakeStreamingClient("Hi"));

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
    }

    [Test]
    public async Task Handle_BlacklistMode_ModelBlacklisted_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var settings = await db.AdminSettings.SingleAsync();
        settings.ActiveModelListMode = ModelListMode.Blacklist;
        db.ModelListEntries.Add(new ModelListEntry { ModelId = "gpt-4", ListType = ModelListType.Blacklist });
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("not available");
    }

    [Test]
    public async Task Handle_PriceCapExceeded_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var settings = await db.AdminSettings.SingleAsync();
        settings.MaxPricePerMillionTokens = 10m;
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user.Id);

        // Model has CompletionPricePerMillion = 15 > cap of 10
        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("not available");
    }

    [Test]
    public async Task Handle_PriceCapNotExceeded_Succeeds()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var settings = await db.AdminSettings.SingleAsync();
        settings.MaxPricePerMillionTokens = 15m;
        await db.SaveChangesAsync();
        var handler = CreateHandler(db, user.Id, chatClientFactory: FakeStreamingClient("Hi"));

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
    }

    [Test]
    public async Task Handle_ProviderFailsMidStream_EmitsErrorNotException()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);

        async IAsyncEnumerable<ChatResponseUpdate> FailingUpdates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            throw new InvalidOperationException("upstream exploded");
        }

        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(FailingUpdates());
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events[0]).IsTypeOf<ChatStreamChunk>();
        await Assert.That(events[^1]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[^1]).Message).Contains("upstream exploded");
    }

    [Test]
    public async Task Handle_WithImageAttachment_LinksAndSendsMultimodal()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var attachment = new Attachment
        {
            UserId = user.Id,
            FileName = "pic.png",
            MediaType = "image/png",
            Data = [1, 2, 3],
            SizeBytes = 3,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        List<ChatMessage>? captured = null;
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(
                Arg.Do<IEnumerable<ChatMessage>>(m => captured = m.ToList()),
                Arg.Any<ChatOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeUpdates("Hi"));
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "What is this?", "gpt-4", [attachment.Id]), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
        var done = (ChatStreamDone)events[^1];
        await Assert.That(done.Result.UserMessage.Attachments.Count).IsEqualTo(1);
        await Assert.That(done.Result.UserMessage.Attachments[0].FileName).IsEqualTo("pic.png");

        var linked = await db.Attachments.SingleAsync(a => a.Id == attachment.Id);
        await Assert.That(linked.MessageId).IsNotNull();

        var sentUserMessage = captured!.Last(m => m.Role == ChatRole.User);
        await Assert.That(sentUserMessage.Contents.OfType<DataContent>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Handle_WebSearchEnabled_UsesOnlineModelVariant()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var factory = FakeStreamingClient("Hi");
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4", WebSearchEnabled: true), CancellationToken.None));

        await Assert.That(events[^1]).IsTypeOf<ChatStreamDone>();
        factory.Received(1).Create("gpt-4:online");
    }

    [Test]
    public async Task Handle_WebSearchDisabled_UsesBaseModel()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var factory = FakeStreamingClient("Hi");
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        factory.Received(1).Create("gpt-4");
    }

    [Test]
    public async Task Handle_ModelSupportsTools_AttachesJsExecutionTool()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db, supportedParameters: ["tools", "temperature"]);

        ChatOptions? captured = null;
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions?>(o => captured = o),
                Arg.Any<CancellationToken>())
            .Returns(FakeUpdates("Hi"));
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Tools!.Count).IsEqualTo(1);
        await Assert.That(captured.Tools![0].Name).IsEqualTo("execute_javascript");
    }

    [Test]
    public async Task Handle_ModelWithoutToolSupport_AttachesNoTools()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);

        ChatOptions? captured = null;
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions?>(o => captured = o),
                Arg.Any<CancellationToken>())
            .Returns(FakeUpdates("Hi"));
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        var handler = CreateHandler(db, user.Id, chatClientFactory: factory);

        await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(captured).IsNull();
    }

    [Test]
    public async Task Handle_UnknownAttachment_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var handler = CreateHandler(db, user.Id);

        var events = await Collect(handler.Handle(new SendMessageCommand(null, "Hello", "gpt-4", [Guid.NewGuid()]), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("attachments");
    }

    // --- Simple factory methods (no logic, just reduce repeated boilerplate) ---

    private static async Task<List<ChatStreamEvent>> Collect(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var list = new List<ChatStreamEvent>();
        await foreach (var e in stream)
        {
            list.Add(e);
        }
        return list;
    }

    private static PolyglotDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<PolyglotDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new PolyglotDbContext(options);
    }

    private static async Task<User> SeedUserWithCredits(PolyglotDbContext db, long credits)
    {
        db.AdminSettings.Add(new AdminSettings());
        var user = new User
        {
            ExternalId = "ext-1",
            Email = "test@test.com",
            CreditBalance = credits,
            Preferences = new UserPreferences()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task SeedModel(PolyglotDbContext db, List<string>? supportedParameters = null)
    {
        db.Models.Add(new Model
        {
            ModelId = "gpt-4",
            Name = "GPT-4",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 15m,
            SupportedParameters = supportedParameters ?? []
        });
        await db.SaveChangesAsync();
    }

    private static IChatClientFactory FakeStreamingClient(params string[] chunks)
    {
        return FakeStreamingClient(promptTokens: 100, completionTokens: 50, chunks: chunks);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FakeUpdates(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
        // Final update with usage + finish reason
        yield return new ChatResponseUpdate
        {
            FinishReason = ChatFinishReason.Stop,
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = 100,
                    OutputTokenCount = 50,
                    TotalTokenCount = 150
                })
            ]
        };
    }

    private static IChatClientFactory FakeStreamingClient(int promptTokens, int completionTokens, string[] chunks)
    {
        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
            // Final update with usage + finish reason
            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop,
                Contents =
                [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = promptTokens,
                        OutputTokenCount = completionTokens,
                        TotalTokenCount = promptTokens + completionTokens
                    })
                ]
            };
        }

        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Updates());

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        return factory;
    }

    private static SendMessageCommandHandler CreateHandler(
        PolyglotDbContext db,
        Guid userId,
        ICreditsService? creditsService = null,
        IChatClientFactory? chatClientFactory = null)
    {
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns(userId);

        return new SendMessageCommandHandler(
            userService,
            db,
            chatClientFactory ?? FakeStreamingClient("ok"),
            creditsService ?? Substitute.For<ICreditsService>(),
            Substitute.For<IChatTitleGenerator>(),
            new JsExecutionService());
    }
}
