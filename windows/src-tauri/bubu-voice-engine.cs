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
                    
                    recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
                    recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(800);
                    recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(800);
                    recognizer.BabbleTimeout = TimeSpan.FromSeconds(2);
                    
                    // ── Wake word ─────────────────────────────────────
                    Choices wakeWords = new Choices(new string[] { 
                        "Hey Bubu", "Hey Pupu", "Halo Bubu", "Halo Pupu" 
                    });
                    GrammarBuilder wakeGb = new GrammarBuilder();
                    wakeGb.Append(wakeWords);
                    Grammar wakeGrammar = new Grammar(wakeGb);
                    wakeGrammar.Name = "WakeWord";
                    
                    // ── Dictation (Sangat akurat jika id-ID terinstall) ────────────────
                    DictationGrammar dictGrammar = new DictationGrammar();
                    dictGrammar.Name = "Dictation";
                    
                    recognizer.LoadGrammar(wakeGrammar);
                    recognizer.LoadGrammar(dictGrammar);
                    
                    wakeGrammar.Enabled = true;
                    dictGrammar.Enabled = false;
                    
                    Timer revertTimer = new Timer((state) => 
                    {
                        if (isListeningCommand)
                        {
                            Console.WriteLine("TIMEOUT");
                            isListeningCommand = false;
                            dictGrammar.Enabled = false;
                            wakeGrammar.Enabled = true;
                        }
                    }, null, Timeout.Infinite, Timeout.Infinite);

                    recognizer.SpeechRecognized += (s, e) =>
                    {
                        string text = (e.Result.Text ?? "").Trim();
                        float conf = e.Result.Confidence;
                        string grammar = e.Result.Grammar.Name;
                        
                        Console.WriteLine("DEBUG_RECOG:" + grammar + ":" + text + "|" + conf);
                        
                        if (!isListeningCommand && grammar == "WakeWord")
                        {
                            Console.WriteLine("WAKE");
                            isListeningCommand = true;
                            wakeGrammar.Enabled = false;
                            dictGrammar.Enabled = true;
                            revertTimer.Change(8000, Timeout.Infinite);
                        }
                        else if (isListeningCommand && grammar == "Dictation")
                        {
                            if (conf >= 0.25f)
                            {
                                revertTimer.Change(Timeout.Infinite, Timeout.Infinite);
                                Console.WriteLine("HEARD:" + text.ToLower());
                                isListeningCommand = false;
                                dictGrammar.Enabled = false;
                                wakeGrammar.Enabled = true;
                            }
                        }
                    };
                    
                    recognizer.SpeechRecognitionRejected += (s, e) =>
                    {
                        string text = (e.Result.Text ?? "").Trim().ToLower();
                        float conf = e.Result.Confidence;
                        
                        if (!isListeningCommand)
                        {
                            if (conf >= 0.25f && (text.Contains("bubu") || text.Contains("pupu")))
                            {
                                Console.WriteLine("WAKE");
                                isListeningCommand = true;
                                wakeGrammar.Enabled = false;
                                dictGrammar.Enabled = true;
                                revertTimer.Change(8000, Timeout.Infinite);
                            }
                        }
                        else
                        {
                            // If rejected but confident enough, accept it anyway
                            if (conf > 0.15f)
                            {
                                revertTimer.Change(Timeout.Infinite, Timeout.Infinite);
                                Console.WriteLine("HEARD:" + text);
                                isListeningCommand = false;
                                dictGrammar.Enabled = false;
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
