// This file is part of CheckmateCoin (PoCM (Proof of Checkmate)
// (c) 2026 by Imusing
// License: MIT

using Chess;
using System.Security.Cryptography; // for public/private key generation

namespace POCM
{
    internal class Blockchain
    {
        private List<Block> ChainInternal { get; set; }
        public List<Block> Chain
        {
            get
            {
                Program.SaveChain(ChainInternal);
                return ChainInternal;
            }
            set
            {
                if (value.Count > 0 && value[0].Hash != GetGenesis().Hash)
                {
                    throw new Exception("Invalid chain: does not start with genesis block");
                }
                ChainInternal = value;
                Program.SaveChain(ChainInternal);
            }
        }
        public static int TargetTime { get; set; } = 60000; // 60 seconds per block

        public Blockchain()
        {
            Chain = new List<Block>();
            Chain.Add(CreateGenesisBlock());
        }

        private Block CreateGenesisBlock()
        {
            Block genesis = new Block(0, 639063950141652850, "e5d3 b4a3 g7b2 a2b2 h8b2", "0", new List<Tx>(), 16027220999649898771);
            genesis.Transactions.Add(new Tx("0", "MIIBCgKCAQEAn11vzmykIANNLo6WinX8kdHcD7QgTe45isg//1wK4oDW+nZT5AEDNzOcL8WAuHpxkXn/OVqqkhxc3+mFLXbEEINN+/4W+U2uD1A6OCfvteh1D7PxMEXs7KcyPvUSSV97Lbt/SiobecGch6Lwg/sdN40coQ88/Gg7oe7qwqIGNZmGDvnEbkdw7+q8w7o/bMPI7v9E0RsS4Y5aZeWFrz2UB2t0lTOv+PLXr7RxSw5ysYfutLUSqtbN/14FQzDaQaaHX5EnwOcHL+KNoeYMz3vTI3CNnll0IgoG/mEdm5FrkvEWCsQp6zSav2Tw5O4eOX2+bmD0h7QfsB5ey3AsA/T0yQIDAQAB", 20, 0, 0));
            genesis.Hash = genesis.CalculateHash();
            if (genesis.Hash != "bnb1nrQQ/rpNpPpBR/pppp4/P3NR2/BkP1P3/1P2P1PK/qq6/8 w - - 0 1")
            {
                throw new Exception("Genesis block hash does not match expected value");
            }
            return genesis;
        }
    
        public Block GetLatestBlock()
        {
            return Chain.Last();
        }

        public bool IsValidMoves(ChessBoard board, string[] moves, int n)
        {
            if (moves.Length != n)
            {
                return false;
            }
            
            // 2-fold repetition is used because otherwise we can just repeat the same move over and over
            // to reach the specified difficulty, obviously this is not a perfect solution but it should be good
            // enough for now.
            List<string> previousPositions = new List<string>();

            foreach (string moveStr in moves)
            {
                Move move = board.Moves().FirstOrDefault(m => m.ToString().Contains(Ucitolongstring(moveStr)));
                if (move == null || !board.IsValidMove(move))
                {
                    return false;
                }
                previousPositions.Add(board.ToFen());
                if (previousPositions.Count(p => p == board.ToFen()) >= 2)
                {
                    return false;
                }
                board.Move(move);
            }
            return true;
        }

        // check if other chain is valid and starts with our genesis block
        public bool IsValidChain(List<Block> otherChain)
        {
            if (otherChain.Count == 0 || otherChain[0].Hash != GetGenesis().Hash)
            {
                return false;
            }
            for (int i = 0; i < otherChain.Count; i++)
            {
                Block currentBlock = otherChain[i];
                Block? previousBlock = i > 0 ? otherChain[i - 1] : null;
                if (currentBlock.Hash != currentBlock.CalculateHash())
                {
                    return false;
                }
                if (i > 0 && currentBlock.PreviousHash != previousBlock.Hash)
                {
                    return false;
                }
                ChessBoard board = ChessBoard.LoadFromFen(currentBlock.CalculateHash());
                string[] moves = currentBlock.Data.Split(' ');
                if (!IsValidMoves(board, moves, currentBlock.Difficulty))
                {
                    return false;
                }
                if (!board.IsEndGame)
                {
                    return false;
                }
            }
            return true;
        }

        private string Ucitolongstring(string moveStr)
        {
            // Convert move string from UCI format (e.g., "e2e4") to long format (e.g., "e2-e4")
            if (moveStr.Length != 4)
            {
                return "Promotion or invalid move format";
            }
            return $"{moveStr.Substring(0, 2)} - {moveStr.Substring(2, 2)}";
        }

        // get public key balance
        public int GetBalance(string address)
        {
            int balance = 0;
            foreach (var block in Chain)
            {
                foreach (var tx in block.Transactions)
                {
                    if (tx.To == address)
                    {
                        balance += (int)tx.Amount;
                    }
                    if (tx.From == address)
                    {
                        balance -= (int)tx.Amount;
                    }
                }
            }
            return balance;
        }

        public Block GetGenesis()
        {
            return Chain[0];
        }

        public bool ValidateAddress(string address)
        {
            try
            {
                byte[] publicKeyBytes = Convert.FromBase64String(address);
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.ImportRSAPublicKey(publicKeyBytes, out _);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool AddBlock(Block newBlock, string? minerAddress = null)
        {
            newBlock.Transactions = newBlock.Transactions.Where(tx => tx.From != "Coinbase").ToList();
            if (minerAddress != null && ValidateAddress(minerAddress))
                newBlock.Transactions.Add(new Tx("Coinbase", minerAddress, 20, newBlock.Index, 0));
            else
                newBlock.Transactions.Add(new Tx("Coinbase", GetGenesis().Transactions[0].To, 20, newBlock.Index, 0));
            newBlock.Timestamp = DateTime.UtcNow.Ticks;
            ChessBoard board = ChessBoard.LoadFromFen(newBlock.CalculateHash());
            string[] moves = newBlock.Data.Split(' ');
            if (!IsValidMoves(board, moves, GetLatestBlock().Difficulty))
            {
                return false;
            }
            if (!board.IsEndGame)
            {
                return false;
            }
            // Simple difficulty changer
            long timeTaken = (newBlock.Timestamp - GetLatestBlock().Timestamp) / TimeSpan.TicksPerMillisecond;
            int difficulty = GetLatestBlock().Difficulty;
            if (timeTaken < TargetTime / 2)
            {
                difficulty++;
                Console.WriteLine($"Increasing difficulty to {difficulty}");
            }
            else if (timeTaken > TargetTime * 2 && difficulty > 1)
            {
                difficulty--;
                Console.WriteLine($"Decreasing difficulty to {difficulty}");
            }
            newBlock.Difficulty = difficulty;
            newBlock.Hash = newBlock.CalculateHash();
            Chain.Add(newBlock);
            return true;
        }
    }
}