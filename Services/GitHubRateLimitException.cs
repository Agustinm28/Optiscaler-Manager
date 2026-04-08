using System;

namespace OptiscalerClient.Services;

/// <summary>
/// Thrown when the GitHub API returns HTTP 403 (rate limit exceeded).
/// </summary>
public class GitHubRateLimitException : Exception
{
    public GitHubRateLimitException()
        : base("GitHub API rate limit exceeded (HTTP 403).")
    {
    }
}
