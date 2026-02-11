using Newtonsoft.Json;

namespace checkmatecoin_cli
{
    internal class Program
    {
        static HttpClient client = new HttpClient();
        static Dictionary<string, string> commandHelp = new Dictionary<string, string>()
        {
            { "getnewaddress", "Generates a new public/private key pair\nUsage: checkmatecoin-cli getnewaddress" },
            { "getbalance", "Gets the balance of the specified address\nUsage: checkmatecoin-cli getbalance <address>" },
            { "sendtoaddress", "Sends the specified amount from one address to another\nUsage: checkmatecoin-cli sendtoaddress <privkey> <to> <amount>\n\n<privkey> is YOUR private key." },
            { "getblockchaininfo", "Gets information about the blockchain\nUsage: checkmatecoin-cli getblockchaininfo" },
            { "getblockhash", "Gets the hash of the block at the specified index\nUsage: checkmatecoin-cli getblockhash <index>" },
            { "getblock", "Gets the block with the specified hash\nUsage: checkmatecoin-cli getblock <hash>" },
            { "generatetoaddress", "Generates the specified number of blocks and sends the reward to the specified address (requires stockfish)\nUsage: checkmatecoin-cli generatetoaddress <nblocks> <address>" }
        };
        static void Main(string[] args)
        {
            List<string> commands = args.ToList();
            if (commands.Count == 0)
            {
                Console.WriteLine("Usage: checkmatecoin-cli <command> [options]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  getnewaddress - Generates a new public/private key pair");
                Console.WriteLine("  getbalance <address> - Gets the balance of the specified address");
                Console.WriteLine("  sendtoaddress <privkey> <to> <amount> - Sends the specified amount from one address to another");
                Console.WriteLine("  getblockchaininfo - Gets information about the blockchain");
                Console.WriteLine("  getblockhash <index> - Gets the hash of the block at the specified index");
                Console.WriteLine("  getblock <hash> - Gets the block with the specified hash");
                Console.WriteLine("  generatetoaddress <nblocks> <address> - Generates the specified number of blocks and sends the reward to the specified address (requires stockfish)");
                return;
            }

            string command = commands[0].ToLower();
            if (!commandHelp.ContainsKey(command))
            {
                Console.WriteLine($"Unknown command: {command}");
                return;
            }
            if (commandHelp.ContainsKey(command) && commands.Count < 2 && (command == "getbalance" || command == "getblockhash" || command == "getblock" || command == "generatetoaddress"))
            {
                Console.WriteLine(commandHelp[command]);
                return;
            }

            var rpcRequest = new
            {
                jsonrpc = "2.0",
                method = command,
                @params = commands.Skip(1).ToArray(),
                id = 1
            };

            var content = new StringContent(JsonConvert.SerializeObject(rpcRequest), System.Text.Encoding.UTF8, "application/json");
            var response = client.PostAsync("http://localhost:19423/", content).Result;
            Console.WriteLine(response.Content.ReadAsStringAsync().Result);
        }
    }
}
