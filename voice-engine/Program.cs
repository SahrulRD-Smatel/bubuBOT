using System;
using System.Threading;
using SherpaOnnx;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;

class Program
{
    static void Main(string[] args)
    {
        Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.WriteLine("DEBUG: Starting Sherpa-ONNX Voice Engine...");

        // Setup Sherpa-ONNX Config
        OnlineRecognizerConfig config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = "model/encoder-epoch-30-avg-9-with-averaged-model.int8.onnx";
        config.ModelConfig.Transducer.Decoder = "model/decoder-epoch-30-avg-9-with-averaged-model.onnx";
        config.ModelConfig.Transducer.Joiner = "model/joiner-epoch-30-avg-9-with-averaged-model.int8.onnx";
        config.ModelConfig.Tokens = "model/tokens.txt";
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;
        // config.ModelConfig.ModelType is implicitly inferred or we leave it

        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 1.2f;
        config.Rule2MinTrailingSilence = 0.8f;
        config.Rule3MinUtteranceLength = 20.0f;

        OnlineRecognizer recognizer;
        try
        {
            recognizer = new OnlineRecognizer(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to load Sherpa-ONNX model. {ex.Message}");
            return;
        }

        OnlineStream stream = recognizer.CreateStream();
        Console.WriteLine("ENGINE_READY");

        unsafe
        {
            var alc = ALContext.GetApi(true);
            if (!alc.TryGetExtension(null, out Capture captureAPI))
            {
                Console.WriteLine("ERROR: OpenAL Capture extension not supported.");
                return;
            }

            var device = captureAPI.CaptureOpenDevice(null, 16000, Silk.NET.OpenAL.BufferFormat.Mono16, 32000);
            if (device == null)
            {
                Console.WriteLine("ERROR: Failed to open OpenAL capture device.");
                return;
            }

            captureAPI.CaptureStart(device);
            Console.WriteLine("DEBUG: Mic capture started at 16kHz.");

            bool wakeWordTriggered = false;
            string lastText = "";

            while (true)
            {
                int samplesAvailable = captureAPI.GetAvailableSamples(device);
                if (samplesAvailable >= 1600) // ~100ms
                {
                    short[] buffer = new short[samplesAvailable];
                    fixed (short* pBuffer = buffer)
                    {
                        captureAPI.CaptureSamples(device, pBuffer, samplesAvailable);
                    }

                    // Convert short[] to float[] for Sherpa-ONNX
                    float[] floatBuffer = new float[samplesAvailable];
                    for (int i = 0; i < samplesAvailable; i++)
                    {
                        floatBuffer[i] = buffer[i] / 32768.0f;
                    }

                    stream.AcceptWaveform(16000, floatBuffer);
                }

                while (recognizer.IsReady(stream))
                {
                    recognizer.Decode(stream);
                }

                string currentText = recognizer.GetResult(stream).Text.ToLower().Trim();

                if (!string.IsNullOrEmpty(currentText) && currentText != lastText)
                {
                    lastText = currentText;
                    
                    // Simple KWS logic
                    if (!wakeWordTriggered && currentText.Contains("bubu"))
                    {
                        Console.WriteLine("WAKE");
                        wakeWordTriggered = true;
                    }
                }

                if (recognizer.IsEndpoint(stream))
                {
                    if (wakeWordTriggered && !string.IsNullOrEmpty(currentText))
                    {
                        // Clean up "bubu" from the command if it's there
                        string command = currentText;
                        int idx = command.IndexOf("bubu");
                        if (idx >= 0)
                        {
                            command = command.Substring(idx + 4).Trim();
                        }
                        
                        if (!string.IsNullOrEmpty(command))
                        {
                            Console.WriteLine($"HEARD: {command}");
                        }
                    }

                    recognizer.Reset(stream);
                    wakeWordTriggered = false;
                    lastText = "";
                }

                Thread.Sleep(20);
            }
        }
    }
}
