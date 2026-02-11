// This file is part of CheckmateCoin (PoCM (Proof of Checkmate)
// (c) 2026 by Imusing
// License: MIT

namespace POCM
{
    public class Tx
    {
        public string Hash { get; set; } // hash
        public string From { get; set; } // public key of sender
        public string To { get; set; } // public key of recipient
        public decimal Amount { get; set; } // amount of checkmatecoin to transfer
        public int Height { get; set; } // height of the block containing this tx
        public long Idx { get; set; } // index of the tx in the block
        public byte[] Signature { get; set; } // must say "checkmate" + "from" + "to" + "amount"

        public Tx(string from, string to, decimal amount, int height, long idx, byte[] signature)
        {
            From = from;
            To = to;
            Amount = amount;
            Height = height;
            Idx = idx;
            Signature = signature;
            CalculateHash();
        }

        public void CalculateHash()
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var rawData = $"{From}{To}{Amount}{Height}{Idx}";
                var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));
                Hash = BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }
    }
}