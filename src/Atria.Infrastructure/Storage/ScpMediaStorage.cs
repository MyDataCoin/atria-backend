using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Atria.Infrastructure.Storage;

/// <summary>
/// <see cref="IMediaStorage"/> that copies files to a remote host over SCP and returns an absolute
/// public URL served by that host's web server. Files are renamed to a UUID (only the extension is
/// kept) so nothing user-controlled reaches the remote path; only the URL is persisted. SSH.NET is
/// synchronous, so the transfer runs on a worker thread and the request stream is buffered first.
/// </summary>
public sealed class ScpMediaStorage : IMediaStorage
{
    private readonly MediaOptions _o;
    private readonly ILogger<ScpMediaStorage> _logger;

    public ScpMediaStorage(IOptions<MediaOptions> options, ILogger<ScpMediaStorage> logger)
    {
        _o = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream content, string fileName, string contentType, string category, CancellationToken ct)
    {
        var cat = SanitizeSegment(category);
        var name = $"{Guid.NewGuid():N}{SafeExtension(fileName)}";
        var remoteDir = $"{_o.RemoteRoot.TrimEnd('/')}/{cat}";
        var remotePath = $"{remoteDir}/{name}";

        // Buffer to memory so the sync SCP upload has a seekable source and the caller can dispose
        // the request stream immediately after this await (files are capped at 25 MB by validators).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        await Task.Run(() =>
        {
            using (var ssh = new SshClient(BuildConnectionInfo()))
            {
                ssh.Connect();
                ssh.RunCommand($"mkdir -p '{remoteDir}'"); // ensure the category folder exists
                ssh.Disconnect();
            }

            using var scp = new ScpClient(BuildConnectionInfo());
            scp.Connect();
            scp.Upload(buffer, remotePath);
            scp.Disconnect();
        }, ct);

        var url = $"{_o.PublicBaseUrl.TrimEnd('/')}/{_o.PublicBasePath.Trim('/')}/{cat}/{name}";
        _logger.LogInformation("Media uploaded via SCP. Remote={Remote} Url={Url}", remotePath, url);
        return url;
    }

    public void Delete(string url)
    {
        var remotePath = ResolveRemotePath(url);
        if (remotePath is null)
            return;

        try
        {
            using var ssh = new SshClient(BuildConnectionInfo());
            ssh.Connect();
            ssh.RunCommand($"rm -f '{remotePath}'");
            ssh.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete remote media {Path}", remotePath);
        }
    }

    // Map a public URL back to {RemoteRoot}/{category}/{name}, allowing ONLY that two-segment shape.
    private string? ResolveRemotePath(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var basePath = $"{_o.PublicBaseUrl.TrimEnd('/')}/{_o.PublicBasePath.Trim('/')}";
        if (!url.StartsWith(basePath, StringComparison.Ordinal))
            return null;

        var parts = url[basePath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        return $"{_o.RemoteRoot.TrimEnd('/')}/{SanitizeSegment(parts[0])}/{Path.GetFileName(parts[1])}";
    }

    private ConnectionInfo BuildConnectionInfo()
    {
        AuthenticationMethod auth = string.IsNullOrWhiteSpace(_o.ScpPrivateKeyPath)
            ? new PasswordAuthenticationMethod(_o.ScpUsername, _o.ScpPassword)
            : new PrivateKeyAuthenticationMethod(_o.ScpUsername, new PrivateKeyFile(_o.ScpPrivateKeyPath));

        return new ConnectionInfo(_o.ScpHost, _o.ScpPort, _o.ScpUsername, auth);
    }

    // Keep a short, alphanumeric extension only; otherwise drop it.
    private static string SafeExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Length is > 1 and <= 10 && ext[1..].All(char.IsLetterOrDigit)
            ? ext.ToLowerInvariant()
            : string.Empty;
    }

    private static string SanitizeSegment(string s)
    {
        var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length > 0 ? clean : "misc";
    }
}
