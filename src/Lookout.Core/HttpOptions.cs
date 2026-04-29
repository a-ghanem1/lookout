namespace Lookout.Core;

/// <summary>Options for outbound <c>HttpClient</c> capture.</summary>
public sealed class HttpOptions
{
    /// <summary>
    /// Master switch for outbound HttpClient capture. Default: <c>true</c>.
    /// Set to <c>false</c> to disable all <c>http-out</c> entry recording.
    /// </summary>
    public bool CaptureOutbound { get; set; } = true;

    /// <summary>
    /// When true, outbound request bodies are captured (content-type gated,
    /// size-capped by <see cref="OutboundBodyMaxBytes"/>). Default: <c>false</c>.
    /// </summary>
    public bool CaptureOutboundRequestBody { get; set; }

    /// <summary>
    /// When true, outbound response bodies are captured (content-type gated,
    /// size-capped by <see cref="OutboundBodyMaxBytes"/>). Default: <c>false</c>.
    /// </summary>
    public bool CaptureOutboundResponseBody { get; set; }

    /// <summary>
    /// Maximum number of bytes captured per outbound request or response body.
    /// Bodies larger than this are truncated with a marker. Default: 65,536 (64 KiB).
    /// </summary>
    public int OutboundBodyMaxBytes { get; set; } = 65_536;
}
