using Polyglot.Infrastructure.Services;

namespace Polyglot.Tests;

public class JsExecutionServiceTests
{
    [Test]
    public async Task Execute_ConsoleLog_CapturesOutput()
    {
        var service = new JsExecutionService();

        var result = service.Execute("console.log('hello', 42);");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Output).IsEqualTo("hello 42");
    }

    [Test]
    public async Task Execute_FinalExpression_IsReturned()
    {
        var service = new JsExecutionService();

        var result = service.Execute("const x = 6 * 7; x");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Output).IsEqualTo("42");
    }

    [Test]
    public async Task Execute_SyntaxError_ReturnsErrorNotThrow()
    {
        var service = new JsExecutionService();

        var result = service.Execute("const = ;");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    public async Task Execute_ThrownJsError_ReturnsErrorMessage()
    {
        var service = new JsExecutionService();

        var result = service.Execute("throw new Error('boom');");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error!).Contains("boom");
    }

    [Test]
    public async Task Execute_InfiniteLoop_TimesOut()
    {
        var service = new JsExecutionService(TimeSpan.FromMilliseconds(250));

        var result = service.Execute("while (true) {}");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error!).Contains("timed out");
    }

    [Test]
    public async Task Execute_NoClrAccess()
    {
        var service = new JsExecutionService();

        // Without AllowClr, the CLR bridge globals must not exist.
        var result = service.Execute("typeof importNamespace + ' ' + typeof System");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Output).IsEqualTo("undefined undefined");
    }

    [Test]
    public async Task Execute_OutputBeforeFailure_IsPreserved()
    {
        var service = new JsExecutionService();

        var result = service.Execute("console.log('step 1'); throw new Error('late');");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Output).Contains("step 1");
    }
}
