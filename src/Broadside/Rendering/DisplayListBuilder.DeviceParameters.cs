using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Overprint control and halftones (ISO 32000-2 §8.6.7, §10.6): recorded before a paint when they differ from what the backend holds
/// at that point of the replay, and saved and restored with the state like the transform, so a backend whose device overprints or
/// halftones can honour them. Recorded through <see cref="SetCompositing(in GraphicsState, Content.ContentContext, bool)"/>, which every paint calls.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayDeviceParameters[] _deviceParameters = new DisplayDeviceParameters[4];
    private int _deviceParameterCount;

    // The parameters the backend holds at this point of the replay, and the ones saved by the open SaveState records.
    private DisplayDeviceParameters _deviceParameter = DisplayDeviceParameters.Initial;
    private DisplayDeviceParameters[] _savedDeviceParameters = new DisplayDeviceParameters[16];
    private int _savedDeviceParameterCount;

    /// <summary>Records the overprint parameters and halftone of <paramref name="state"/> before a paint, when they differ from the backend's.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.7 (<c>OP</c>, <c>op</c>, <c>OPM</c>) and §10.6.5 (<c>HT</c>, <c>HTO</c>), Table 52. Both overprint flags are
    /// recorded; the backend picks the stroking one for strokes, as it does with the alpha constants.
    /// </remarks>
    private void SetDeviceParameters(in GraphicsState state)
    {
        SetDeviceParameters(DisplayDeviceParameters.FromState(state));
    }

    /// <summary>Records <paramref name="parameters"/> when they differ from the backend's (also for a Type 3 description's replay).</summary>
    private void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
        if (parameters == _deviceParameter)
        {
            return;
        }

        _deviceParameter = parameters;
        AddOperation(new DisplayOperation(DisplayOperationKind.SetDeviceParameters, default, _deviceParameterCount, 0));
        Append(ref _deviceParameters, ref _deviceParameterCount, parameters);
    }

    private void SaveDeviceParameters() => Append(ref _savedDeviceParameters, ref _savedDeviceParameterCount, _deviceParameter);

    private void RestoreDeviceParameters() => _deviceParameter = _savedDeviceParameters[--_savedDeviceParameterCount];

    private void ResetDeviceParameters()
    {
        // The halftones are document objects: forget them so nothing pins a document.
        Array.Clear(_deviceParameters, 0, _deviceParameterCount);
        Array.Clear(_savedDeviceParameters, 0, _savedDeviceParameterCount);
        _deviceParameterCount = 0;
        _deviceParameter = DisplayDeviceParameters.Initial;
        _savedDeviceParameterCount = 0;
    }
}
