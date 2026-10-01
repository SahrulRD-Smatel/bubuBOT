using System;
using System.Speech.Recognition;
using System.Threading;

namespace BubuVoiceEngine
{
    class Program
    {
        static bool isListeningCommand = false;
        
        static void Main(string[] args)
        {
            try
            {
                SpeechRecognitionEngine recognizer;
                try
                {
                    recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("id-ID"));
                }
                catch
                {
                    recognizer = new SpeechRecognitionEngine();
                }

                using (recognizer)
                {
                    recognizer.SetInputToDefaultAudioDevice();
                    Console.WriteLine("DEBUG:CULTURE:" + recognizer.RecognizerInfo.Culture.Name);
                    
                    // Make SAPI extremely snappy and prevent buffering lags
                    recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
                    recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(500);
                    recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(500);
                    recognizer.BabbleTimeout = TimeSpan.FromSeconds(2);
                    
                    // 1. Wake word grammar — keep it simple and light
                    Choices wakeWords = new Choices();
                    wakeWords.Add(new string[] { 
                        "Hey Bubu", "Hey Pupu", "Halo Bubu", "Halo Pupu", 
                        "Nihao Bubu", "Nihao Pupu" 
                    });
                    GrammarBuilder wakeGb = new GrammarBuilder();
                    wakeGb.Append(wakeWords);
                    Grammar wakeGrammar = new Grammar(wakeGb);
                    wakeGrammar.Name = "WakeWord";
                    
                    DictationGrammar commandsGrammar = new DictationGrammar();
                    commandsGrammar.Name = "Commands";
                    
                    recognizer.LoadGrammar(wakeGrammar);
                    recognizer.LoadGrammar(commandsGrammar);
                    
                    wakeGrammar.Enabled = true;
                    commandsGrammar.Enabled = false;
                    
                    Timer revertTimer = new Timer((state) => 
                    {
                        if (isListeningCommand)
                        {
                            Console.WriteLine("TIMEOUT");
                            isListeningCommand = false;
                            commandsGrammar.Enabled = false;
                            wakeGrammar.Enabled = true;
                        }
                    }, null, Timeout.Infinite, Timeout.Infinite);

                    recognizer.SpeechRecognized += (s, e) =>
                    {
                        string text = (e.Result.Text ?? "").Trim();
                        float conf = e.Result.Confidence;
                        
                        Console.WriteLine("DEBUG_RECOG:" + e.Result.Grammar.Name + ":" + text + "|" + conf);
                        
                        if (!isListeningCommand && e.Result.Grammar.Name == "WakeWord")
                        {
                            // Accept ANY confidence for wake word from SpeechRecognized
                            Console.WriteLine("WAKE");
                            isListeningCommand = true;
                            wakeGrammar.Enabled = false;
                            commandsGrammar.Enabled = true;
                            revertTimer.Change(10000, Timeout.Infinite);
                        }
                        else if (isListeningCommand && e.Result.Grammar.Name == "Commands")
                        {
                            // Accept ANY confidence for commands from SpeechRecognized
                            revertTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            Console.WriteLine("HEARD:" + text);
                            isListeningCommand = false;
                            commandsGrammar.Enabled = false;
                            wakeGrammar.Enabled = true;
                        }
                    };
                    
                    // Also rescue rejected results
                    recognizer.SpeechRecognitionRejected += (s, e) =>
                    {
                        string text = (e.Result.Text ?? "").Trim();
                        float conf = e.Result.Confidence;
                        
                        if (!string.IsNullOrEmpty(text))
                        {
                            Console.WriteLine("DEBUG_REJECT:" + text + "|" + conf);
                        }
                        
                        if (!isListeningCommand)
                        {
                            // Check if rejected result looks like a wake word
                            string lower = text.ToLower();
                            if (conf >= 0.0f && (lower.Contains("bubu") || lower.Contains("pupu")))
                            {
                                Console.WriteLine("WAKE");
                                isListeningCommand = true;
                                wakeGrammar.Enabled = false;
                                commandsGrammar.Enabled = true;
                                revertTimer.Change(10000, Timeout.Infinite);
                            }
                        }
                        else
                        {
                            // In command mode, accept rejected results that look like commands
                            string lower = text.ToLower();
                            if (conf > 0.001f && (lower.StartsWith("buka") || lower.StartsWith("cari") || lower.StartsWith("open") || lower.StartsWith("search")))
                            {
                                revertTimer.Change(Timeout.Infinite, Timeout.Infinite);
                                Console.WriteLine("HEARD:" + text);
                                isListeningCommand = false;
                                commandsGrammar.Enabled = false;
                                wakeGrammar.Enabled = true;
                            }
                        }
                    };
                    
                    Console.WriteLine("ENGINE_READY");
                    
                    recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    
                    Thread.Sleep(Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR:" + ex.Message);
            }
        }
    }
}
