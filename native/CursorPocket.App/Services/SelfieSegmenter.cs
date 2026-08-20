using CursorPocket.Core.Media;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CursorPocket_App.Services;

/// <summary>
/// Person segmentation over the MediaPipe Selfie Segmenter ONNX model
/// (Apache-2.0, fetched with a pinned hash like the FFmpeg sidecar). One
/// instance per preview session; inference runs on the frame thread with
/// reused buffers so the steady state allocates nothing.
/// </summary>
public sealed class SelfieSegmenter : IPersonMaskModel, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int[] _inputShape;
    private readonly float[] _inputBuffer;
    private bool _outputIsLogits;
    private bool _outputRangeChecked;
    private bool _failed;

    public int InputSize { get; }

    public bool ChannelsFirst { get; }

    private SelfieSegmenter(InferenceSession session)
    {
        _session = session;
        var input = session.InputMetadata.First();
        var output = session.OutputMetadata.First();
        _inputName = input.Key;
        _outputName = output.Key;
        var dims = input.Value.Dimensions;
        if (dims.Length != 4)
        {
            throw new InvalidOperationException($"Unexpected segmentation input rank {dims.Length}.");
        }
        // [1,3,H,W] is channels-first; [1,H,W,3] is channels-last. Dynamic axes
        // report as -1, so fall back to the nominal 256 the model was built for.
        ChannelsFirst = dims[1] == 3;
        var side = ChannelsFirst ? dims[2] : dims[1];
        InputSize = side > 0 ? side : 256;
        _inputShape = ChannelsFirst ? [1, 3, InputSize, InputSize] : [1, InputSize, InputSize, 3];
        // Reused every frame: at 30fps a fresh tensor would churn ~23 MB/s
        // through the collector in the middle of a recording.
        _inputBuffer = new float[InputSize * InputSize * 3];
    }

    /// <summary>Returns null instead of throwing: a missing or broken model only disables effects.</summary>
    public static SelfieSegmenter? TryCreate(string modelPath)
    {
        try
        {
            if (!File.Exists(modelPath))
            {
                return null;
            }
            var options = new SessionOptions
            {
                // The mask is 256² on a small preview; two threads is plenty and
                // leaves the rest of the machine to the screen encoder.
                IntraOpNumThreads = 2,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            return new SelfieSegmenter(new InferenceSession(modelPath, options));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool TryGetMask(ReadOnlySpan<float> inputTensor, Span<float> mask)
    {
        if (_failed)
        {
            return false;
        }
        try
        {
            inputTensor.CopyTo(_inputBuffer);
            var tensor = new DenseTensor<float>(_inputBuffer, _inputShape);
            using var results = _session.Run(
                [NamedOnnxValue.CreateFromTensor(_inputName, tensor)],
                [_outputName]);
            var output = results[0].AsEnumerable<float>();
            var index = 0;
            foreach (var value in output)
            {
                if (index >= mask.Length)
                {
                    break;
                }
                mask[index++] = value;
            }
            if (!_outputRangeChecked)
            {
                // Some exports emit logits rather than sigmoid probabilities.
                // Decide once from the first frame and stick with it.
                _outputRangeChecked = true;
                foreach (var value in mask)
                {
                    if (value < -0.01f || value > 1.01f)
                    {
                        _outputIsLogits = true;
                        break;
                    }
                }
            }
            if (_outputIsLogits)
            {
                for (var i = 0; i < mask.Length; i++)
                {
                    mask[i] = 1f / (1f + MathF.Exp(-mask[i]));
                }
            }
            return true;
        }
        catch (Exception)
        {
            // One bad inference disables segmentation for the session rather
            // than crashing the self-view mid-recording.
            _failed = true;
            return false;
        }
    }

    public void Dispose() => _session.Dispose();

    /// <summary>The model sidecar staged next to the executable by the build, like ffmpeg.exe.</summary>
    public static string ResolveModelPath()
    {
        var besideApp = Path.Combine(AppContext.BaseDirectory, "selfie_segmenter.onnx");
        if (File.Exists(besideApp))
        {
            return besideApp;
        }
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "third_party", "models", "selfie_segmenter.onnx"));
    }
}
