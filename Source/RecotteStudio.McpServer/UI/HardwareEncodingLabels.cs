namespace RecotteStudio.McpServer.UI;

/// <summary>Names shown in the settings window for the hardware encoding setting and the encoders.</summary>
internal static class HardwareEncodingLabels
{
    /// <summary>The settings in the order of <see cref="HardwareEncoding"/>, so a list index is the value.</summary>
    public static readonly HardwareEncoding[] Settings = Enum.GetValues<HardwareEncoding>();

    public static string Name(HardwareEncoding value) => value switch
    {
        HardwareEncoding.Auto => "自動（使える GPU を探す）",
        HardwareEncoding.Off => "使わない（CPU: libx264）",
        HardwareEncoding.Nvidia => "NVIDIA（NVENC）",
        HardwareEncoding.Intel => "Intel（Quick Sync Video）",
        HardwareEncoding.Amd => "AMD（AMF）",
        _ => value.ToString(),
    };

    public static string Name(VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvidia => "NVIDIA NVENC",
        VideoEncoder.Intel => "Intel Quick Sync Video",
        VideoEncoder.Amd => "AMD AMF",
        _ => "CPU",
    } + $"（{VideoEncoders.CodecName(encoder)}）";
}
