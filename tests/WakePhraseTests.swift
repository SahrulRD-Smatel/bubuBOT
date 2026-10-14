import Foundation

// MARK: - WakePhrase tests (pure functions, no concurrency)

@main
enum WakePhraseTests {

    static var failures = 0

    static func main() {
        // MARK: split — should match

        // Basic wake phrases (no command)
        checkMatch("ok bubu",              WakePhrase.split("ok bubu"),              cmd: "")
        checkMatch("OK BUBU",              WakePhrase.split("OK BUBU"),              cmd: "")
        checkMatch("okay bubu",            WakePhrase.split("okay bubu"),            cmd: "")
        checkMatch("ok kuku",                WakePhrase.split("ok kuku"),                cmd: "")
        checkMatch("ok cou cou",             WakePhrase.split("ok cou cou"),             cmd: "")
        checkMatch("hey bubu",             WakePhrase.split("hey bubu"),             cmd: "")
        checkMatch("Hey Bubu",             WakePhrase.split("Hey Bubu"),             cmd: "")

        // Command extracted from original text (casing/punctuation preserved)
        checkMatch("Okay, Bubu, ajoute GitHub",
                   WakePhrase.split("Okay, Bubu, ajoute GitHub"),             cmd: "ajoute GitHub")
        checkMatch("ok cuckoo add github",
                   WakePhrase.split("ok cuckoo add github"),                    cmd: "add github")
        checkMatch("ok bubu ouvre figma",
                   WakePhrase.split("ok bubu ouvre figma"),                   cmd: "ouvre figma")

        // Apostrophes, hyphens and accents preserved in command
        checkMatch("OK Bubu, mets l'album de Daft Punk",
                   WakePhrase.split("OK Bubu, mets l'album de Daft Punk"),   cmd: "mets l'album de Daft Punk")
        checkMatch("ok bubu qu'est-ce que fait Claude ?",
                   WakePhrase.split("ok bubu qu'est-ce que fait Claude ?"),  cmd: "qu'est-ce que fait Claude ?")

        // Wake anywhere in transcript (word-boundary search)
        checkMatch("euh ok bubu mets de la musique",
                   WakePhrase.split("euh ok bubu mets de la musique"),        cmd: "mets de la musique")
        checkMatch("la chanson dit ok bubu",
                   WakePhrase.split("la chanson dit ok bubu"),                cmd: "") // accepted false positive

        // LAST occurrence wins
        checkMatch("ok bubu search ok bubu play music",
                   WakePhrase.split("ok bubu search ok bubu play music"),   cmd: "play music")

        // Partial word-by-word delivery
        checkNoMatch("ok alone",             WakePhrase.split("ok"))
        checkMatch("ok bubu partial",      WakePhrase.split("ok bubu"),              cmd: "")
        checkMatch("ok bubu with more",    WakePhrase.split("ok bubu mets"),         cmd: "mets")

        // MARK: split — should NOT match
        checkNoMatch("standalone bubu",    WakePhrase.split("bubu"))
        checkNoMatch("standalone BUBU",    WakePhrase.split("BUBU"))
        checkNoMatch("partial bubu ça va", WakePhrase.split("bubu ça va"))
        checkNoMatch("bubu comment",       WakePhrase.split("bubu comment tu vas"))
        checkNoMatch("ok google",            WakePhrase.split("ok google"))
        checkNoMatch("hey siri",             WakePhrase.split("hey siri"))
        checkNoMatch("okkkk bubu",         WakePhrase.split("okkkk bubu"))
        checkNoMatch("empty",                WakePhrase.split(""))

        // Summary
        if failures == 0 { print("\n\(total)/\(total) tests passed.") }
        else { print("\n\(failures) test(s) FAILED."); exit(1) }
    }

    static var total = 0

    static func checkMatch(_ label: String, _ r: WakePhrase.SplitResult, cmd: String) {
        total += 1
        if !r.matched {
            print("✗  \(label) — expected match, got no match"); failures += 1; return
        }
        if r.command != cmd {
            print("✗  \(label) — matched ✓ but command \"\(r.command)\" ≠ \"\(cmd)\""); failures += 1; return
        }
        print("✓  \(label)")
    }

    static func checkNoMatch(_ label: String, _ r: WakePhrase.SplitResult) {
        total += 1
        if r.matched { print("✗  \(label) — expected no match, got matched (cmd: \"\(r.command)\")"); failures += 1 }
        else         { print("✓  \(label)") }
    }
}
