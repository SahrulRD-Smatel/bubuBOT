using System;
using System.IO;
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

        // Auto-discover model directory
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "model"),
            "model",
            Path.Combine(baseDir, "..", "..", "src-tauri", "model"),
            Path.Combine(baseDir, "..", "..", "..", "voice-engine", "model"),
            Path.Combine(baseDir, "..", "..", "..", "windows", "src-tauri", "model"),
            @"f:\Dhani\JOB\botAI\bubuBOT\voice-engine\model",
            @"f:\Dhani\JOB\botAI\bubuBOT\windows\src-tauri\model"
        };

        string modelDir = "model";
        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "tokens.txt")))
            {
                modelDir = Path.GetFullPath(c);
                break;
            }
        }
        Console.WriteLine($"DEBUG: Using model directory: {modelDir}");

        // Setup Sherpa-ONNX Config
        OnlineRecognizerConfig config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, "encoder-epoch-30-avg-9-with-averaged-model.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder-epoch-30-avg-9-with-averaged-model.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner-epoch-30-avg-9-with-averaged-model.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;

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
            DateTime wakeUntil = DateTime.MinValue;
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
                    Console.WriteLine($"DEBUG_HEARD: {currentText}");

                    // Normalise text: strip all punctuation, whitespace and special characters
                    string clean = new string(currentText.ToLower().Where(char.IsLetter).ToArray());

                    // Check wake word variants: bubu, pupu, puru, buru, buku, puku, kupu, kuku, coucou, bobo, popo
                    bool isWake = clean.Contains("bubu") || clean.Contains("pupu") ||
                                  clean.Contains("puru") || clean.Contains("buru") ||
                                  clean.Contains("buku") || clean.Contains("puku") ||
                                  clean.Contains("kupu") || clean.Contains("kuku") ||
                                  clean.Contains("bobo") || clean.Contains("popo") ||
                                  clean.Contains("coucou") ||
                                  (clean.Length <= 5 && (clean.Contains("bu") || clean.Contains("pu"))) ||
                                  (clean.Contains("alo") && (clean.Contains("pu") || clean.Contains("bu") || clean.Contains("ku") || clean.Contains("ru"))) ||
                                  (clean.Contains("halo") && (clean.Contains("pu") || clean.Contains("bu") || clean.Contains("ku") || clean.Contains("ru"))) ||
                                  (clean.Contains("hey") && (clean.Contains("pu") || clean.Contains("bu") || clean.Contains("ku") || clean.Contains("ru"))) ||
                                  (clean.Contains("hai") && (clean.Contains("pu") || clean.Contains("bu") || clean.Contains("ku") || clean.Contains("ru"))) ||
                                  currentText.Contains("bubu") || currentText.Contains("pupu") ||
                                  currentText.Contains("puru") || currentText.Contains("buru") ||
                                  currentText.Contains("buku") || currentText.Contains("puku") || currentText.Contains("kupu");

                    if (isWake)
                    {
                        wakeUntil = DateTime.UtcNow.AddSeconds(10);
                        if (!wakeWordTriggered)
                        {
                            Console.WriteLine("WAKE");
                            wakeWordTriggered = true;
                        }
                    }
                }

                if (recognizer.IsEndpoint(stream))
                {
                    bool isWithinWakeSession = (DateTime.UtcNow <= wakeUntil) || wakeWordTriggered;
                    if (isWithinWakeSession && !string.IsNullOrEmpty(currentText))
                    {
                        // Clean up wake words from the command if present
                        string command = currentText;
                        foreach (var w in new[] { "halo bubu", "halo pupu", "halo buku", "alo bubu", "hai bubu", "hey bubu", "bubu", "pupu", "buku", "puku", "kupu", "buru", "puru", "bobo", "popo" })
                        {
                            int idx = command.IndexOf(w);
                            if (idx >= 0)
                            {
                                command = (command.Substring(0, idx) + " " + command.Substring(idx + w.Length)).Trim();
                            }
                        }

                        command = command.Trim();
                        if (!string.IsNullOrEmpty(command) && command.Length > 2)
                        {
                            Console.WriteLine($"HEARD: {command}");
                            wakeUntil = DateTime.MinValue;
                            wakeWordTriggered = false;
                        }
                    }

                    recognizer.Reset(stream);
                    if (DateTime.UtcNow > wakeUntil)
                    {
                        wakeWordTriggered = false;
                    }
                    lastText = "";
                }

                Thread.Sleep(20);
            }
        }
    }
}

