using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NSubstitute;
using Polyglot.Application.Command;
using Polyglot.Application.Dtos;
using Polyglot.Application.Services;
using Polyglot.Domain;
using Polyglot.Domain.Enums;
using Polyglot.Infrastructure;
using Polyglot.Infrastructure.Extensions;
using Polyglot.Infrastructure.Services;

namespace Polyglot.Tests;

public class ChatStreamServiceTests
{
    [Test]
    public async Task Stream_NotEnoughCredits_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 5);
        await SeedModel(db);

        var creditsService = Substitute.For<ICreditsService>();
        creditsService
            .EstimateChatCreditsAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(100L);

        var service = CreateService(db, user.Id, creditsService);

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("Insufficient credits");
    }

    [Test]
    public async Task Stream_NotEnoughCredits_NeverCallsAI()
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
        var service = CreateService(db, user.Id, creditsService, factory);

        await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        chatClient.DidNotReceive().GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Stream_Success_EmitsChunksThenDone()
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

        var service = CreateService(db, user.Id, creditsService, FakeStreamingClient("Hello", " world"));

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hi", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events[0]).IsTypeOf<ChatStreamChunk>();
        await Assert.That(((ChatStreamChunk)events[0]).Text).IsEqualTo("Hello");
        await Assert.That(events[1]).IsTypeOf<ChatStreamChunk>();
        await Assert.That(((ChatStreamChunk)events[1]).Text).IsEqualTo(" world");
        await Assert.That(events[2]).IsTypeOf<ChatStreamDone>();
    }

    [Test]
    public async Task Stream_Success_DeductsActualCostNotEstimate()
    {
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

        var service = CreateService(db, user.Id, creditsService, FakeStreamingClient("Response"));

        await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        var checkDb = CreateDb(dbName);
        var updatedUser = await checkDb.Users.SingleAsync(u => u.Id == user.Id);
        await Assert.That(updatedUser.CreditBalance).IsEqualTo(880);
    }

    [Test]
    public async Task Stream_Success_UsesActualTokensFromAIResponse()
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

        var service = CreateService(db, user.Id, creditsService, FakeStreamingClient(promptTokens: 250, completionTokens: 75, chunks: ["Hi"]));

        await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await creditsService.Received(1).CalculateChatCreditsAsync(
            250, 75, 3m, 15m, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Stream_LockedUser_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        user.IsLocked = true;
        await db.SaveChangesAsync();
        await SeedModel(db);
        var service = CreateService(db, user.Id);

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("locked");
    }

    [Test]
    public async Task Stream_NullModel_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        var service = CreateService(db, user.Id);

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", null), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("Model is required");
    }

    [Test]
    public async Task Stream_UnknownModel_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var service = CreateService(db, user.Id);

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(null, "Hello", "nonexistent"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("not found");
    }

    [Test]
    public async Task Stream_NonexistentChat_EmitsError()
    {
        var db = CreateDb();
        var user = await SeedUserWithCredits(db, credits: 10_000);
        await SeedModel(db);
        var service = CreateService(db, user.Id);

        var events = await Collect(service.StreamMessageAsync(new SendMessageCommand(Guid.NewGuid(), "Hello", "gpt-4"), CancellationToken.None));

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<ChatStreamError>();
        await Assert.That(((ChatStreamError)events[0]).Message).Contains("Chat not found");
    }

    // --- helpers ---

    private static async Task<List<ChatStreamEvent>> Collect(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var list = new List<ChatStreamEvent>();
        await foreach (var e in stream) list.Add(e);
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

    private static async Task SeedModel(PolyglotDbContext db)
    {
        db.Models.Add(new Polyglot.Domain.Model
        {
            ModelId = "gpt-4",
            Name = "GPT-4",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 15m
        });
        await db.SaveChangesAsync();
    }

    private static IChatClientFactory FakeStreamingClient(params string[] chunks)
        => FakeStreamingClient(promptTokens: 100, completionTokens: 50, chunks: chunks);

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
            var final = new ChatResponseUpdate
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
            yield return final;
        }

        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Updates());

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(chatClient);
        return factory;
    }

    private static ChatStreamService CreateService(
        PolyglotDbContext db,
        Guid userId,
        ICreditsService? creditsService = null,
        IChatClientFactory? chatClientFactory = null)
    {
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns(userId);

        return new ChatStreamService(
            userService,
            db,
            chatClientFactory ?? FakeStreamingClient("ok"),
            creditsService ?? Substitute.For<ICreditsService>());
    }
}
