using Chess;
using Newtonsoft.Json;

namespace POCM
{
    internal class Block
    {
        public int Difficulty { get; set; } = 4; // mate in 2 exactly ( 4 plies = 2 moves )
        public int Index { get; set; }
        public long Timestamp { get; set; }
        public string Data { get; set; }
        public string PreviousHash { get; set; }
        public string Hash { get; set; }
        public ulong Nonce { get; set; }
        public List<Tx> Transactions { get; set; } = new List<Tx>();
        public List<Tx> PreviousTxs { get; set; } = new List<Tx>();
        public Block(int index, long timestamp, string data, string previousHash, List<Tx> previousTxs, ulong nonce)
        {
            Index = index;
            Timestamp = timestamp;
            Data = data;
            PreviousHash = previousHash;
            PreviousTxs = previousTxs;
            Nonce = nonce;
            Hash = CalculateHash();
        }
        [JsonConstructor]
        public Block(int difficulty, int index, long timestamp, string data, string previousHash, ulong nonce, List<Tx> transactions, List<Tx> previousTxs)
        {
            Difficulty = difficulty;
            Index = index;
            Timestamp = timestamp;
            Data = data;
            PreviousHash = previousHash;
            Nonce = nonce;
            Transactions = transactions;
            PreviousTxs = previousTxs;
            Hash = CalculateHash();
        }
        public string CalculateHash()
        {
            bool success = false;
            string fen = "";
            while (!success)
            {
                success = true;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var rawData = $"{Index}{PreviousHash}{Nonce}{Transactions.Count}";
                    var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));
                    Random rnd = new Random(BitConverter.ToInt32(bytes, 0));
                    // random chess position
                    string[] pieces = { "Q", "R", "B", "N", "P", "q", "r", "b", "n", "p" };
                    double[] pieceWeights = { 0.02, 0.05, 0.09, 0.1, 0.2, 0.02, 0.05, 0.09, 0.1, 0.2 };
                    int[] pieceMax = { 2, 2, 2, 2, 7, 2, 2, 2, 2, 7 };
                    // fen
                    fen = "";
                    for (int i = 0; i < 8; i++)
                    {
                        int emptyCount = 0;
                        for (int j = 0; j < 8; j++)
                        {
                            double rand = rnd.NextDouble();
                            string piece = "";
                            for (int k = 0; k < pieces.Length; k++)
                            {
                                if (rand < pieceWeights[k] && fen.Count(c => c == pieces[k][0]) < pieceMax[k])
                                {
                                    piece = pieces[k];
                                    break;
                                }
                                rand -= pieceWeights[k];
                            }
                            // no pawns on the first and last rank
                            if ((i == 0 || i == 7) && (piece == "P" || piece == "p"))
                            {
                                piece = "";
                            }
                            if (piece == "")
                            {
                                emptyCount++;
                            }
                            else
                            {
                                if (emptyCount > 0)
                                {
                                    fen += emptyCount.ToString();
                                    emptyCount = 0;
                                }
                                fen += piece;
                            }
                        }
                        if (emptyCount > 0)
                        {
                            fen += emptyCount.ToString();
                        }
                        if (i < 7)
                        {
                            fen += "/";
                        }
                    }
                    // add kings at random empty squares
                    List<int> emptySquares = new List<int>();
                    for (int i = 0; i < fen.Length; i++)
                    {
                        if (char.IsDigit(fen[i]))
                        {
                            int emptyCount = fen[i] - '0';
                            emptySquares.Add(i);
                        }
                    }
                    if (emptySquares.Count > 1)
                    {
                        // place white king and decrease empty count
                        int whiteKingSquare = emptySquares[rnd.Next(emptySquares.Count)];
                        fen = fen.Substring(0, whiteKingSquare) + "K" + fen.Substring(whiteKingSquare + 1);
                        emptySquares.Remove(whiteKingSquare);
                        // place black king and decrease empty count
                        int blackKingSquare = emptySquares[rnd.Next(emptySquares.Count)];
                        fen = fen.Substring(0, blackKingSquare) + "k" + fen.Substring(blackKingSquare + 1);
                        emptySquares.Remove(blackKingSquare);
                        // make sure the fen doesn't have more than 16 pieces (or like /K8/)
                        int pieceCount = fen.Count(c => char.IsLetter(c));
                        if (pieceCount > 32)
                        {
                            Nonce++;
                            success = false;
                            continue;
                        }
                        string[] ranks = fen.Split('/');
                        bool continueOuter = false;
                        for (int i = 0; i < ranks.Length; i++)
                        {
                            int rankPieceCount = ranks[i].Count(c => char.IsLetter(c));
                            int rankEmptyCount = 0;

                            foreach (char c in ranks[i])
                            {
                                if (char.IsDigit(c))
                                {
                                    rankEmptyCount += c - '0';
                                }
                            }
                            if (rankPieceCount > 8 || rankEmptyCount > 8 || rankPieceCount + rankEmptyCount != 8)
                            {
                                Nonce++;
                                success = false;
                                continueOuter = true;
                                break;
                            }
                        }
                        if (continueOuter)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        Nonce++;
                        success = false;
                        continue;
                    }
                    if (ChessBoard.LoadFromFen(fen + " w - - 0 1").IsEndGame && Index != 0)
                    {
                        Nonce++;
                        success = false;
                        continue;
                    }
                    if (ChessBoard.LoadFromFen(fen + " w - - 0 1").BlackKingChecked)
                    {
                        Nonce++;
                        success = false;
                        continue;
                    }
                }
            }
            return fen + " w - - 0 1"; // add dummy move info to make it look like a real fen
        }
    }
}