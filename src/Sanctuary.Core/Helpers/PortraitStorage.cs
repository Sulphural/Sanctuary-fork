using System;
using System.IO;

namespace Sanctuary.Core.Helpers;

public static class PortraitStorage
{
    public static uint GetPortraitCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }

        return ~crc;
    }

    // Set the same absolute directory for Gateway and WebAPI when hosted separately.
    public static string GetCharacterDirectory(ulong guid)
    {
        var root = Environment.GetEnvironmentVariable("SANCTUARY_PORTRAIT_IMAGE_ROOT");
        return Path.Combine(string.IsNullOrWhiteSpace(root) ? "Images" : root, guid.ToString());
    }

    public static string GetPendingDirectory(ulong guid, uint portraitCrc)
    {
        return Path.Combine(GetCharacterDirectory(guid), "pending", portraitCrc.ToString("x8"));
    }

    public static bool HasLivePortrait(ulong guid, uint portraitCrc)
    {
        return ReadCrc(Path.Combine(GetCharacterDirectory(guid), "portrait.crc")) == portraitCrc;
    }

    public static bool TryPromotePendingUpload(ulong guid, uint portraitCrc)
    {
        var pendingDirectory = GetPendingDirectory(guid, portraitCrc);

        if (ReadCrc(Path.Combine(pendingDirectory, "portrait.crc")) != portraitCrc)
            return false;

        var pendingPortrait = Path.Combine(pendingDirectory, "portrait.png");

        if (!File.Exists(pendingPortrait))
            return false;

        var characterDirectory = GetCharacterDirectory(guid);
        var pendingHeadshot = Path.Combine(pendingDirectory, "headshot.png");

        if (File.Exists(pendingHeadshot))
            File.Move(pendingHeadshot, Path.Combine(characterDirectory, "headshot.png"), true);

        File.Move(pendingPortrait, Path.Combine(characterDirectory, "portrait.png"), true);
        File.WriteAllBytes(Path.Combine(characterDirectory, "portrait.crc"), BitConverter.GetBytes(portraitCrc));

        Directory.Delete(pendingDirectory, true);

        return true;
    }

    public static void DeleteStalePendingUploads(ulong guid, TimeSpan maxAge)
    {
        var pendingRoot = Path.Combine(GetCharacterDirectory(guid), "pending");

        if (!Directory.Exists(pendingRoot))
            return;

        var cutoff = DateTime.UtcNow - maxAge;

        foreach (var directory in Directory.GetDirectories(pendingRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static uint? ReadCrc(string path)
    {
        if (!File.Exists(path))
            return null;

        var data = File.ReadAllBytes(path);

        return data.Length == sizeof(uint) ? BitConverter.ToUInt32(data) : null;
    }
}
