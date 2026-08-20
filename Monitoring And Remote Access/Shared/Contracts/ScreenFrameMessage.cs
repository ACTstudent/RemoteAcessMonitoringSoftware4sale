namespace Shared.Contracts;

public sealed record ScreenFrameMessage(
    string StudentId,
    string PcName,
    string FrameBase64,
    DateTime Timestamp);
