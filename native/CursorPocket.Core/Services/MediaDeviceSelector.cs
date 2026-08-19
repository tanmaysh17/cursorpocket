using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public static class MediaDeviceSelector
{
    public static MediaDeviceDescriptor? SelectRemembered(IEnumerable<MediaDeviceDescriptor> devices, string? rememberedName)
    {
        var available = devices.ToList();
        return available.FirstOrDefault(device => string.Equals(device.Name, rememberedName, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(device => device.IsDefault)
            ?? available.FirstOrDefault();
    }
}
