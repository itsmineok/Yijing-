namespace Yijing.Infrastructure.KataGo;

public interface IKataGoTransport : IAsyncDisposable
{
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
}
