namespace POCM
{
    public class Tx
    {
        public string Hash { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Amount { get; set; }
        public int Height { get; set; }
        public int Idx { get; set; }
        public Tx(string from, string to, decimal amount, int height, int idx)
        {
            From = from;
            To = to;
            Amount = amount;
            Height = height;
            Idx = idx;
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