namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// The harness retries an adb step, and the CI job the whole test step, only when the transport failed (issue #344); an error the
/// device reported must fail at once. These need no device.
/// </summary>
public sealed class AdbTests
{
    [Theory]
    [InlineData("adb: device offline")]
    [InlineData("error: device 'emulator-5554' not found")]
    [InlineData("adb: error: failed to read copy response")]
    [InlineData("error: closed")]
    [InlineData("error: protocol fault (couldn't read status): Connection reset by peer")]
    [InlineData("[ 65%] /sdcard/Android/data/com.broadside.rendering.android.device/files/render/dct-cmyk.page0.outlines.bgra: 64%\n")]
    [InlineData("pull: building file list...\n")]
    [InlineData("")]
    public void A_dropped_transport_is_retried(string output) => Assert.True(Adb.LooksLikeTransport(output));

    [Theory]
    [InlineData("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE: Failed to override installation location]")]
    [InlineData("adb: error: failed to stat remote object '/sdcard/x': No such file or directory")]
    [InlineData("Exception occurred while executing 'install':")]
    [InlineData("rm: /sdcard/x: Permission denied")]
    public void An_error_the_device_reported_is_not_retried(string output) => Assert.False(Adb.LooksLikeTransport(output));
}
