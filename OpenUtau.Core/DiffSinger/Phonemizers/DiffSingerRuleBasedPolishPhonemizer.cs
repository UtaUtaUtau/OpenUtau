

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.G2p {
    public class RuleBasedPolishG2p : IG2p {
        private readonly Regex kAllPunct = new Regex(@"^[\p{P}]$");
        
        private readonly string[] validPhonemes = [
            "A", "C", "E", "H", "J", "L", "N", "S", "X", "Z", "a", "b", "c", "cz", "d", "dX", "dZ", "dz", "e", "f", "g", "gs",
            "h", "hh", "i", "j", "k", "l", "m", "n", "ng", "o", "p", "r", "s", "sz", "t", "u", "w", "y", "z"
        ];

        private readonly string[] glides = ["L", "j"];

        private readonly string[] vowels = ["A", "a", "E", "e", "i", "o", "u", "y"];
        
        private readonly string[] sibilants = ["S", "s", "sz", "X", "Z", "z"];

        private readonly string[] unvoiced = ["p", "t", "C", "c", "cz", "k", "h", "f", "S", "s", "sz"];

        private readonly string[] vowelsHH = ["A", "a", "E", "e", "o"];

        private readonly Dictionary<char, string[]> alternate = new() {
            { 'ą', ["A"] },
            { 'ę', ["E"]},
            { 'ć', ["C"] },
            { 'ł', ["L"] },
            { 'ń', ["N"] },
            { 'ś', ["S"] },
            { 'ź', ["X"] },
            { 'ż', ["Z"] },
            { 'q', ["k"] },
            { 'x', ["k", "s"] },
            { 'v', ["w"] },
            { '\'', ["gs"] }
        };

        private readonly Dictionary<char, char[]> digraphTree = new() {
            { 'c', ['h', 'z'] },
            { 'd', ['z', 'ź', 'ż'] },
            { 's', ['z'] },
            { 'r', ['z'] } 
        };

        private readonly Dictionary<string, string> digraphs = new() {
            { "cz", "cz" },
            { "dź", "dX" },
            { "dż", "dZ" },
            { "dz", "dz" },
            { "sz", "sz" },
            { "ch", "h" },
            { "rz", "Z" }
        };

        private readonly Dictionary<string, string> palatalize = new() {
            { "dz", "dX" },
            { "c", "C" },
            { "n", "N" },
            { "s", "S" },
            { "z", "X" }
        };

        private readonly Dictionary<string, string> nasalize = new() {
            { "S", "J" },
            { "X", "J" },
            { "b", "m" },
            { "f", "m" },
            { "p", "m" },
            { "w", "m" },
            { "c", "n" },
            { "cz", "n" },
            { "d", "n" },
            { "dz", "n" },
            { "dZ", "n" },
            { "t", "n" },
            { "C", "N" },
            { "dX", "N" },
            { "g", "ng" },
            { "h", "ng" },
            { "H", "ng" },
            { "k", "ng" }
        };

        private readonly Dictionary<string, string> devoice = new() {
            { "b", "p" }, 
            { "d", "t" }, 
            { "dX", "C" }, 
            { "dz", "c" }, 
            { "dZ", "cz" }, 
            { "g", "k" }, 
            { "H", "h" }, 
            { "w", "f" }, 
            { "X", "S" }, 
            { "z", "s" }, 
            { "Z", "sz" }
        };
        
        public bool IsGlide(string symbol) => glides.Contains(symbol);

        public bool IsValidSymbol(string symbol) => validPhonemes.Contains(symbol);

        public bool IsVowel(string symbol) => vowels.Contains(symbol);

        bool IsConsonant(string symbol) => !symbol.Equals("|") && !vowels.Contains(symbol);
        
        public string[] UnpackHint(string hint, char separator = ' ') {
            return hint.Split(separator)
                .Where(x => validPhonemes.Contains(x))
                .ToArray();
        }
        
        public string[] Query(string grapheme) {
            if (string.IsNullOrEmpty(grapheme) || kAllPunct.IsMatch(grapheme)) {
                return null;
            }
            return Predict(grapheme);
        }

        string[]? Predict(string grapheme) {
            List<string> phonemes = [];
            
            // 0. remove punctuation/whitespace and apply lowercase
            grapheme = grapheme.ToLower(new CultureInfo("pl-PL"));
            grapheme = grapheme.Replace(" ", "");
            
            // 1. rough g2p pass
            for (int i = 0; i < grapheme.Length; i++) {
                char c = grapheme[i];
                switch (c) {
                    default:
                        if (alternate.TryGetValue(c, out var p)) {
                            phonemes.AddRange(p);
                        } else if (digraphTree.TryGetValue(c, out var nextC)) {
                            if (i + 1 < grapheme.Length) {
                                if (nextC.Contains(grapheme[i + 1])) {
                                    string digraph = new string([c, grapheme[i + 1]]);
                                    if (digraphs.TryGetValue(digraph, out var p2)) {
                                        phonemes.Add(p2);
                                        i++;
                                    }
                                } else {
                                    phonemes.Add(c.ToString());
                                }
                            } else {
                                phonemes.Add(c.ToString());
                            }
                        } else {
                            phonemes.Add(c.ToString());
                        }
                        break;
                }
            }
            
            // only contain valid phonemes and digraph bypass
            phonemes = phonemes.Where(x => IsValidSymbol(x) || x.Equals("|")).ToList();
            
            // final E -> e
            if (phonemes.Last().Equals("E")) phonemes[^1] = "e";
                  
            // 2. palatalization pass
            for (int i = phonemes.Count - 2; i > 0; i--) {
                string prev = phonemes[i - 1];
                string curr = phonemes[i];
                string next = phonemes[i + 1];
                
                // extra: hh VCV
                if (IsVowel(prev) && curr.Equals("h") && vowelsHH.Contains(next)) {
                    phonemes[i] = "hh";
                }
                
                if (!curr.Equals("i") || IsVowel(prev)) continue;

                if (IsVowel(next)) {
                    if (palatalize.TryGetValue(prev, out var pal)) {
                        phonemes[i - 1] = pal;
                        phonemes.RemoveAt(i);
                    } else {
                        phonemes[i] = "j";
                    }
                } else {
                    if (palatalize.TryGetValue(prev, out var pal)) {
                        phonemes[i - 1] = pal;
                    }
                }
            }
            
            // 3. nasalization pass
            for (int i = phonemes.Count - 2; i >= 0; i--) {
                string curr = phonemes[i];
                string next = phonemes[i + 1];
                switch (curr)
                {
                    // A. Global
                    case "A" or "E" when IsConsonant(next): 
                        if (nasalize.TryGetValue(next, out var nasal)) {
                            phonemes[i] = (curr.Equals("A")) ? "o" : "e";
                            phonemes.Insert(i + 1, nasal);
                        }
                        break;
                    // B. N edge case
                    case "N" when sibilants.Contains(next):
                        phonemes[i] = "J";
                        break;
                }
            }
            
            // 4. devoice pass
            // unvoiced assimilation
            for (int i = 1; i < phonemes.Count; i++) {
                string prev = phonemes[i - 1];
                string curr = phonemes[i];
                if (!unvoiced.Contains(prev)) continue;

                if (devoice.TryGetValue(curr, out var uv)) {
                    phonemes[i] = uv;
                }
            }
            
            // w/z devoice
            if ((phonemes[0].Equals("w") || phonemes[0].Equals("z")) && unvoiced.Contains(phonemes[1])) {
                if (phonemes[0].Equals("w")) phonemes[0] = "f";
                if (phonemes[0].Equals("z")) phonemes[0] = "s";
            }
            
            // final devoice
            for (int i = phonemes.Count - 1; i >= 0; i--) {
                string curr = phonemes[i];
                if (IsVowel(curr)) break;
                if (unvoiced.Contains(curr)) continue;
                if (devoice.TryGetValue(curr, out var dv)) {
                    phonemes[i] = dv;
                } else {
                    break;
                }
            }
            
            return phonemes.Where(x => !x.Equals("|")).ToArray();
        }
    }
}

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Rule-based Polish Phonemizer", "DIFFS PL", "UtaUtaUtau and PixPrucer", "PL")]
    public class DiffSingerRuleBasedPolishPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-pl.yaml";

        public override string GetLangCode() => "pl";

        protected override IG2p LoadBaseG2p() => new RuleBasedPolishG2p();

        protected override string[] GetBaseG2pVowels() => new string[] {
            "A", "a", "E", "e", "i", "o", "u", "y"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "C", "c", "cz", "d", "dX", "dZ", "dz", "f", "g", "gs", "H", "h", "hh", "J", "j", "k", "L", "l", "m", "N",
            "n", "ng", "p", "r", "S", "s", "sz", "t", "w", "X", "Z", "z"
        };
    }
}

