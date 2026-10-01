using System;
using System.Speech.Recognition;

namespace BubuVoice
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
                    recognizer.LoadGrammar(new DictationGrammar());
                    
                    Console.WriteLine("READY");
                    
                    RecognitionResult result = recognizer.Recognize(TimeSpan.FromSeconds(5));
                    if (result != null)
                    {
                        Console.WriteLine("HEARD:" + result.Text);
                    }
                    else
                    {
                        Console.WriteLine("TIMEOUT");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR:" + ex.Message);
            }
        }
    }
}
