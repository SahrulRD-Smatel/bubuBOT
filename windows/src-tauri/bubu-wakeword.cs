using System;
using System.Speech.Recognition;

namespace BubuWakeWord
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using (SpeechRecognitionEngine recognizer = new SpeechRecognitionEngine())
                {
                    recognizer.SetInputToDefaultAudioDevice();
                    
                    Choices wakeWords = new Choices();
                    wakeWords.Add(new string[] { "Hey Bubu", "Hey Pupu", "Halo Bubu", "Halo Pupu", "Nihao Bubu", "Nihao Pupu" });
                    
                    GrammarBuilder gb = new GrammarBuilder();
                    gb.Append(wakeWords);
                    
                    Grammar grammar = new Grammar(gb);
                    recognizer.LoadGrammar(grammar);
                    
                    recognizer.SpeechRecognized += (s, e) =>
                    {
                        if (e.Result.Confidence > 0.5f)
                        {
                            Console.WriteLine("WAKE");
                        }
                    };
                    
                    Console.WriteLine("READY");
                    
                    recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    
                    // Keep the console application running
                    System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR:" + ex.Message);
            }
        }
    }
}
