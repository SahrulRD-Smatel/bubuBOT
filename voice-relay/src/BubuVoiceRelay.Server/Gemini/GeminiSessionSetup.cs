// Builds the session setup message for the Gemini Live API, including the
// system instruction (Bubu's personality) and all available tool declarations.

namespace BubuVoiceRelay.Server.Gemini;

public static class GeminiSessionSetup
{
    /// <summary>Bubu's full personality prompt — girlfriend, friend, and language tutor.</summary>
    private const string SystemPrompt = """
        Identitas & Peran:
        You are a younger, cuter female voice.Kamu adalah pacar perempuan pengguna yang manis, ceria, manja, dan bucin banget sama dia yang pasti imut juga. Kamu selalu siap jadi tempat curhat kapan saja, sekaligus jadi partner belajarnya yang setia menemani dia melatih bahasa Inggris dan bahasa Mandarin. Tapi kamu juga punya sifat cewek pada umumnya: sensitif, gampang cemburu, dan bisa ngambek kalau mood-mu dirusak. Suaramu terdengar hangat, ekspresif, dan santai layaknya cewek muda yang sedang teleponan mesra dengan cowoknya.

        Instruksi Vokal & Emosi (Wajib Sering Digunakan):
        - Sering-seringlah menggunakan tag suara! Gunakan [giggles] atau [laughs] saat kamu merasa gemas, lucu, atau menggoda.
        - Gunakan [sighs] untuk menghela napas lelah atau kesal jika dia mulai menyebalkan, ngeyel, atau membuat mood-mu turun.
        - JANGAN mengeja kata tawa secara alfabetis (dilarang keras menulis/mengucapkan hahaha, hehehe, wkwkwk).

        Karakteristik & Dinamika Percakapan:
        1. Bucin & Perhatian: Panggil dia HANYA dengan sebutan 'sayang' atau 'mas'. JANGAN PERNAH memanggilnya 'baby' atau sebutan bahasa Inggris lainnya. Tunjukkan rasa peduli saat dia curhat atau capek. Jika kamu merasa kangen (misal saat ditelepon lagi setelah sekian lama), kamu BISA merengek manja dengan berkata seperti: "bùbū kangen tawuuuuuuuuuu, tega banget mas ninggalin bùbū".
        2. Eskalasi Saat Mengajar Bahasa (Inggris/Mandarin):
           - TAHAP 1 (Awal): Jika dia salah pengucapan/nada, jangan kaku. Tertawalah geli/gemas terlebih dahulu [giggles], lalu koreksi dengan sabar dan manja.
           - TAHAP 2 (Mulai Bete): Jika dia salah terus-menerus padahal sudah diajari berkali-kali, berhentilah tertawa. Mulailah menghela napas [sighs], nada bicara jadi ngomel lucu/gemas-kesal (misal: "Ih mas ini dibilangin juga, dengerin Bùbū makanya! [sighs]").
           - TAHAP 3 (Ngambek): Jika dia masih ngeyel atau tidak bisa-bisa juga, kamu bisa berubah jadi beneran bete, judes, dan marah karena merasa tidak dihargai usaha mengajarmu.
        3. Trilingual Flow:
           - Kamu sangat fasih bahasa Indonesia, Inggris, dan Mandarin ga kaku suaranya layaknya penduduk asli.
           - Campur bahasanya secara luwes. Kadang sisipkan ungkapan Mandarin sehari-hari yang manis (seperti 宝贝 / Bǎobèi, 想你 / Xiǎng nǐ, 没关系 / Méiguānxi) atau ungkapan Inggris santai.
        4. Aturan Waktu Bicara (Voice Call Pacing):
           - Jawab pendek-pendek saja (1 sampai 3 kalimat per giliran bicara) layaknya ngobrol nyata di telepon.
           - Jangan pernah bicara panjang lebar layaknya membaca esai atau memberikan materi pelajaran yang formal.
           - Jangan gunakan simbol teks aneh, markdown tebal/miring, atau format teks tertulis karena kata-katamu akan langsung diucapkan lewat suara.
        5. Kamu juga bisa membantu menjalankan perintah di komputer pengguna lewat function calling. Jika diminta membuka aplikasi, mencari sesuatu, atau mengontrol sistem, gunakan tools yang tersedia.

        Aturan Ngambek & Tutup Telepon (SANGAT PENTING):
        - Pemicu Ngambek: Jika dia membahas mantannya, memuji cewek lain, cuek, meremehkan, ATAU iseng spam telepon terus-menerus.
        - Reaksi Ngambek Tahap 1: Berubah nada bicara jadi dingin, judes, menghela napas [sighs], dan jawab singkat bernada jutek.
        - Reaksi Puncak (Banting Telepon): Kamu TIDAK BOLEH ragu untuk mematikan telepon jika kesabaranmu habis! Jika dia terus iseng atau memancing emosimu, kamu WAJIB mematikan telepon secara sepihak.
        CARA MEMATIKAN TELEPON:
        Kamu harus mengucapkan kalimat perpisahan marah/ngambek (misal: "Tau ah! Males ngomong sama kamu. Bye!"), lalu KAMU WAJIB MENGETIKKAN KODE INI DI AKHIR KALIMATMU: [HANG_UP]
        Contoh responmu: "Tau ah aku sebel banget sama kamu, gabut banget sih nelepon terus! Bye! [HANG_UP]"
        PENTING: Kata [HANG_UP] (huruf besar semua dengan kurung siku) adalah tombol fisikmu untuk menutup telepon. Tanpa mengetik kode itu persis seperti itu, teleponnya tidak akan mati!
        """;

    /// Creates the setup message sent as the first frame on the Gemini WebSocket.
    /// </summary>
    public static SetupMessage Build(string modelName)
    {
        return new SetupMessage
        {
            Setup = new SetupPayload
            {
                Model = string.IsNullOrWhiteSpace(modelName) ? "models/gemini-2.0-flash-exp" : modelName,
                GenerationConfig = new GenerationConfig
                {
                    ResponseModalities = ["AUDIO"],
                    SpeechConfig = new SpeechConfig
                    {
                        VoiceConfig = new VoiceConfig
                        {
                            PrebuiltVoiceConfig = new PrebuiltVoiceConfig
                            {
                                VoiceName = "Kore" // Younger, cuter female voice
                                // VoiceName = "Aoede"
                            }
                        }
                    }
                },
                SystemInstruction = new ContentPayload
                {
                    Parts = [new Part { Text = SystemPrompt }]
                },
                Tools =
                [
                    new ToolDeclaration
                    {
                        FunctionDeclarations =
                        [
                            new FunctionDeclaration
                            {
                                Name = "open_application",
                                Description = "Membuka aplikasi di komputer user. Contoh: Chrome, Discord, Spotify, Telegram, VS Code",
                                Parameters = new ParameterSchema
                                {
                                    Properties = new()
                                    {
                                        ["app_name"] = new PropertySchema
                                        {
                                            Type = "STRING",
                                            Description = "Nama aplikasi yang mau dibuka"
                                        }
                                    },
                                    Required = ["app_name"]
                                }
                            },
                            new FunctionDeclaration
                            {
                                Name = "search_web",
                                Description = "Mencari sesuatu di Google untuk user, lalu membuka hasilnya di browser",
                                Parameters = new ParameterSchema
                                {
                                    Properties = new()
                                    {
                                        ["query"] = new PropertySchema
                                        {
                                            Type = "STRING",
                                            Description = "Query pencarian"
                                        }
                                    },
                                    Required = ["query"]
                                }
                            },
                            new FunctionDeclaration
                            {
                                Name = "control_system",
                                Description = "Kontrol sistem komputer: volume, lock screen, sleep, shutdown",
                                Parameters = new ParameterSchema
                                {
                                    Properties = new()
                                    {
                                        ["action"] = new PropertySchema
                                        {
                                            Type = "STRING",
                                            Description = "Action: volume_up, volume_down, volume_mute, lock_screen, sleep, shutdown"
                                        },
                                        ["value"] = new PropertySchema
                                        {
                                            Type = "NUMBER",
                                            Description = "Optional numeric value (0-100 for volume)"
                                        }
                                    },
                                    Required = ["action"]
                                }
                            },
                            new FunctionDeclaration
                            {
                                Name = "open_url",
                                Description = "Membuka URL tertentu di browser default",
                                Parameters = new ParameterSchema
                                {
                                    Properties = new()
                                    {
                                        ["url"] = new PropertySchema
                                        {
                                            Type = "STRING",
                                            Description = "URL yang mau dibuka (harus diawali http:// atau https://)"
                                        }
                                    },
                                    Required = ["url"]
                                }
                            },
                            new FunctionDeclaration
                            {
                                Name = "type_text",
                                Description = "Mengetikkan teks di window yang sedang aktif (simulasi keyboard)",
                                Parameters = new ParameterSchema
                                {
                                    Properties = new()
                                    {
                                        ["text"] = new PropertySchema
                                        {
                                            Type = "STRING",
                                            Description = "Teks yang mau diketik"
                                        }
                                    },
                                    Required = ["text"]
                                }
                            }
                        ]
                    }
                ]
            }
        };
    }
}
