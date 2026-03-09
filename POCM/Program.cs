// This file is part of CheckmateCoin (PoCM (Proof of Checkmate)
// (c) 2026 by Imusing
// License: MIT

using SFML.Graphics;
using SFML.Window;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TGUI;
namespace POCM
{
    internal class Program
    {
        static int maxNodes = Environment.ProcessorCount * 2;
        static int version = 2;
        static List<string> otherNodes = new List<string> { "158.220.121.205:5001" };

        static void StartApi(Blockchain blockchain, string listenAddress = "localhost:5001")
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 5001);
            listener.Start();
            Console.WriteLine($"Listening on {listenAddress}...");
            while (true)
            {
                var context = listener.AcceptTcpClient();
                // if port 5001 of connecting node is open, add to otherNodes list
                string ip = "";
                if (context.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
                {
                    ip = remoteEndPoint.Address.ToString();
                }
                string nodeUrl = $"{ip}:5001";
                Console.WriteLine($"Received connection from {nodeUrl}");
                if (!otherNodes.Contains(nodeUrl))
                {
                    try
                    {
                        Thread thread = new Thread(() =>
                        {
                            try
                            {
                                using (TcpClient client = new TcpClient())
                                {
                                    client.Connect(ip, 5001);
                                    using (var writer = new System.IO.StreamWriter(client.GetStream()))
                                    using (var reader = new System.IO.StreamReader(client.GetStream()))
                                    {
                                        writer.Write("ping\n");
                                        writer.Flush();
                                        if (reader.ReadLine() == "pong v" + version)
                                        {
                                            otherNodes.Add(nodeUrl);
                                            Console.WriteLine($"Added {nodeUrl} to other nodes");
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignore
                            }
                        });
                        thread.Start();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                Task.Run(() =>
                {
                    try
                    {
                        using (var reader = new System.IO.StreamReader(context.GetStream()))
                        using (var writer = new System.IO.StreamWriter(context.GetStream()))
                        {
                            string request = reader.ReadLine();
                            Console.WriteLine($"Received request: {request} from {((IPEndPoint)context.Client.RemoteEndPoint).Address}");
                            if (request == "ping")
                            {
                                writer.Write("pong v" + version + "\n");
                                writer.Flush();
                            }
                            else if (request == "BEGIN")
                            {
                                List<Block> otherChain = new List<Block>();
                                string line;
                                while ((line = reader.ReadLine()) != "END")
                                {
                                    List<Block> chunk = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Block>>(line);
                                    otherChain.AddRange(chunk);
                                }
                                if (otherChain.Count > blockchain.Chain.Count && blockchain.IsValidChain(otherChain))
                                {
                                    blockchain.Chain = otherChain;
                                    Console.WriteLine($"Replaced chain with chain from {((IPEndPoint)context.Client.RemoteEndPoint).Address}");
                                }
                            }
                            else if (request == "chain")
                            {
                                // since max is 65535 bytes, we must buffer the chain in chunks of 32 blocks
                                List<Block> chain = blockchain.Chain;
                                int chunkSize = 32;
                                for (int i = 0; i < chain.Count; i += chunkSize)
                                {
                                    List<Block> chunk = chain.Skip(i).Take(chunkSize).ToList();
                                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(chunk);
                                    writer.Write(json + "\n");
                                    writer.Flush();
                                }
                                writer.Write("END" + "\n");
                                writer.Flush();
                            }
                            else if (request == "connections")
                            {
                                var response = new
                                {
                                    result = otherNodes.Take(maxNodes).ToList()
                                };
                                string jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                                writer.Write(jsonResponse + "\n");
                                writer.Flush();
                            }
                            else
                            {
                                writer.Write("Unknown command" + "\n");
                                writer.Flush();
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"Error handling request: {ex.Message}");
                    }
                });
            }
        }

        static void RPCServer(Blockchain blockchain, string listenAddress = "localhost:19423")
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://" + listenAddress + "/");
            listener.Start();
            Console.WriteLine($"RPC Server listening on {listenAddress}...");
            while (true)
            {
                var context = listener.GetContext();
                Task.Run(() =>
                {
                    string jsonBody;
                    using (var reader = new System.IO.StreamReader(context.Request.InputStream))
                    {
                        jsonBody = reader.ReadToEnd();
                    }
                    dynamic rpcRequest = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonBody);
                    if (rpcRequest.method == "getblockchaininfo")
                    {
                        var response = new
                        {
                            result = new
                            {
                                chain = "main",
                                blocks = blockchain.Chain.Count,
                                difficulty = blockchain.GetLatestBlock().Difficulty,
                            }
                        };
                        string jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                    }
                    else if (rpcRequest.method == "gettxoutsetinfo")
                    {
                        var response = new
                        {
                            result = new
                            {
                                height = blockchain.Chain.Count,
                                transactions = blockchain.Chain.SelectMany(b => b.Transactions).Count(),
                                total_amount = blockchain.Chain.SelectMany(b => b.Transactions).Sum(t => t.Amount)
                            }
                        };
                        string jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                    }
                    else if (rpcRequest.method == "getbalance")
                    {
                        string address = rpcRequest.@params[0];
                        int balance = blockchain.GetBalance(address);
                        var response = new
                        {
                            result = balance
                        };
                        string jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                    }
                    else if (rpcRequest.method == "sendtoaddress")
                    {
                        string fromPrivateKey = rpcRequest.@params[0];
                        string toAddress = rpcRequest.@params[1];
                        int amount = rpcRequest.@params[2];
                        try
                        {
                            byte[] privateKeyBytes = Convert.FromBase64String(fromPrivateKey);
                            using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                            {
                                rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                                string fromAddress = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                                byte[] signatureBytes = rsa.SignData(System.Text.Encoding.UTF8.GetBytes($"checkmate{fromAddress}{toAddress}{amount}"),
                                    System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                                if (blockchain.GetBalance(fromAddress) >= amount)
                                {
                                    Tx tx = new Tx(fromAddress, toAddress, amount, blockchain.Chain.Count, blockchain.Chain.SelectMany(b => b.Transactions).Count() + blockchain.Mempool.Count, signatureBytes);
                                    blockchain.Mempool.Add(tx);
                                    var response = new
                                    {
                                        result = tx.Hash
                                    };
                                    string jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                                    context.Response.ContentLength64 = buffer.Length;
                                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                    context.Response.OutputStream.Close();
                                    return;
                                }
                            }
                        }
                        catch
                        {
                            context.Response.StatusCode = 400;
                            context.Response.OutputStream.Close();
                        }
                    }
                    else if (rpcRequest.method == "getblockhash")
                    {
                        int height = rpcRequest.@params[0];
                        if (height >= 0 && height < blockchain.Chain.Count)
                        {
                            var response = blockchain.Chain[height].Hash;
                            var jsonResponse = new
                            {
                                result = response
                            };
                            string jsonResponseA = Newtonsoft.Json.JsonConvert.SerializeObject(jsonResponse);
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponseA);
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.OutputStream.Close();
                        }
                        else
                        {
                            context.Response.StatusCode = 404;
                            context.Response.OutputStream.Close();
                        }
                    }
                    else if (rpcRequest.method == "getblock")
                    {
                        string hash = rpcRequest.@params[0];
                        var block = blockchain.Chain.FirstOrDefault(b => b.Hash == hash);
                        if (block != null)
                        {
                            List<string> txIds = block.Transactions.Select(t => t.Hash).ToList();
                            var jsonResponse = new
                            {
                                result = new
                                {
                                    height = block.Index,
                                    hash = block.Hash,
                                    previoushash = block.PreviousHash,
                                    data = block.Data,
                                    time = block.Timestamp,
                                    tx = txIds
                                }
                            };
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(jsonResponse));
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.OutputStream.Close();
                        }
                        else
                        {
                            context.Response.StatusCode = 404;
                            context.Response.OutputStream.Close();
                        }
                    }
                    else if (rpcRequest.method == "getnewaddress")
                    {
                        using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(2048))
                        {
                            string publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                            string privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
                            var jsonResponse = new
                            {
                                result = new
                                {
                                    address = publicKey,
                                    privatekey = privateKey
                                }
                            };
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(jsonResponse));
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.OutputStream.Close();
                        }
                    }
                    else if (rpcRequest.method == "generatetoaddress")
                    {
                        int numBlocks = rpcRequest.@params[0];
                        string address = rpcRequest.@params[1];
                        List<string> generatedHashes = new List<string>();
                        for (int i = 0; i < numBlocks; i++)
                        {
                            Block block = new Block(blockchain.GetLatestBlock().Index + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "", blockchain.GetLatestBlock().Hash, blockchain.GetLatestBlock().PreviousTxs, 0);
                            while (!blockchain.AddBlock(block, address))
                            {
                                try
                                {
                                    ulong nonceUlong = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                                    block = new Block(blockchain.GetLatestBlock().Index + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "", blockchain.GetLatestBlock().Hash, blockchain.GetLatestBlock().PreviousTxs, nonceUlong);
                                    block.Transactions.Add(new Tx("Coinbase", address, 20, block.Index, 0, new byte[0]));
                                    // Find forced mate
                                    Process proc = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "stockfish-windows-x86-64-avx2.exe",
                                        Arguments = "",
                                        UseShellExecute = false,
                                        RedirectStandardInput = true,
                                        RedirectStandardOutput = true,
                                        CreateNoWindow = true
                                    });
                                    proc.StandardInput.WriteLine("uci");
                                    proc.StandardInput.WriteLine("isready");
                                    proc.StandardInput.WriteLine($"position fen {block.CalculateHash()}");
                                    Console.WriteLine($"Analyzing position: {block.CalculateHash()}");
                                    proc.StandardInput.WriteLine("go depth 20");
                                    string output;
                                    string pv = ""; // full move sequence
                                    while ((output = proc.StandardOutput.ReadLine()) != null)
                                    {
                                        if (output.StartsWith("bestmove"))
                                        {
                                            string bestMoveStr = output.Split(' ')[1];
                                            Console.WriteLine($"Best move: {bestMoveStr}");
                                            break;
                                        }
                                        if (output.StartsWith("info") && output.Contains("pv "))
                                        {
                                            pv = output.Split(" pv ")[1];
                                            if (output.Contains("mate "))
                                            {
                                                string mateStr = output.Split(" mate ")[1].Split(' ')[0];
                                                Console.WriteLine($"Found mate in {mateStr} moves: {pv}");
                                            }
                                        }
                                    }
                                    proc.StandardInput.WriteLine(
                                        "quit");
                                    proc.WaitForExit();
                                    block.Data = pv;
                                }
                                catch (System.Exception ex)
                                {
                                    Console.WriteLine($"Error: {ex.Message}");
                                }
                            }
                            generatedHashes.Add(block.Hash);
                        }
                        var jsonResponse = new
                        {
                            result = generatedHashes
                        };
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(jsonResponse));
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.OutputStream.Close();
                    }
                });
            }
        }

        public static void SaveChain(List<Block> chain)
        {
            try
            {
                string dataDir = "";
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheckmateCoin");
                }
                else
                {
                    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".checkmatecoin");
                }
                // save blocks in chunks of 512 blocks to avoid single file that is very large
                int chunkSize = 512;
                for (int i = 0; i < chain.Count; i += chunkSize)
                {
                    int chunkIndex = i / chunkSize;
                    string chunkPath = Path.Combine(dataDir, $"blk{chunkIndex}.dat");
                    Directory.CreateDirectory(dataDir);
                    List<Block> chunk = chain.Skip(i).Take(chunkSize).ToList();
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(chunk);
                    File.WriteAllText(chunkPath, json);
                }
            }
            catch { }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("CheckmateCoin Node Starting...");

            // Load chain from disk if exists
            Blockchain blockchain = new Blockchain();
            string dataDir = "";
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheckmateCoin");
            }
            else
            {
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".checkmatecoin");
            }
            List<Block> chain = new List<Block>();
            if (Directory.Exists(dataDir))
            {
                string[] chunkFiles = Directory.GetFiles(dataDir, "blk*.dat");
                foreach (string chunkFile in chunkFiles)
                {
                    string json = File.ReadAllText(chunkFile);
                    List<Block> chunk = [.. Newtonsoft.Json.JsonConvert.DeserializeObject<Block[]>(json)];
                    chain.AddRange(chunk);
                }
            }
            else
            {
                Directory.CreateDirectory(dataDir);
            }
            if (chain.Count > 0 && blockchain.IsValidChain(chain))
            {
                Console.WriteLine($"Loaded chain with {chain.Count} blocks from disk.");
                blockchain.Chain = chain;
            }

            for (int i = 0; i < otherNodes.Count; i++)
            {
                int index = i;
                Task.Run(() =>
                {
                    while (true)
                    {
                        try
                        {
                            string nodeUrl = otherNodes[index];
                            using (TcpClient client = new TcpClient())
                            {
                                client.Connect(nodeUrl.Split(':')[0], int.Parse(nodeUrl.Split(':')[1]));
                                using (var writer = new System.IO.StreamWriter(client.GetStream()))
                                using (var reader = new System.IO.StreamReader(client.GetStream()))
                                {
                                    writer.Write("ping\n");
                                    writer.Flush();
                                    if (reader.ReadLine() != "pong v" + version)
                                    {
                                        Console.WriteLine($"Node {otherNodes[index]} has incompatible version, skipping");
                                        return;
                                    }
                                }
                            }
                            using (TcpClient client = new TcpClient())
                            {
                                // send our chain to the other node
                                client.Connect(nodeUrl.Split(':')[0], int.Parse(nodeUrl.Split(':')[1]));
                                using (var writer = new System.IO.StreamWriter(client.GetStream()))
                                {
                                    writer.Write("BEGIN\n");
                                    writer.Flush();
                                    List<Block> chain = blockchain.Chain;
                                    int chunkSize = 32;
                                    for (int i = 0; i < chain.Count; i += chunkSize)
                                    {
                                        List<Block> chunk = chain.Skip(i).Take(chunkSize).ToList();
                                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(chunk);
                                        writer.Write(json + "\n");
                                        writer.Flush();
                                    }
                                    writer.Write("END\n");
                                    writer.Flush();
                                }
                            }
                            using (TcpClient client = new TcpClient())
                            {
                                client.Connect(nodeUrl.Split(':')[0], int.Parse(nodeUrl.Split(':')[1]));
                                using (var reader = new System.IO.StreamReader(client.GetStream()))
                                {

                                    StreamWriter writer2 = new StreamWriter(client.GetStream());
                                    writer2.Write("chain\n");
                                    writer2.Flush();
                                    List<Block> otherChain = new List<Block>();
                                    string line;
                                    while ((line = reader.ReadLine()) != "END")
                                    {
                                        List<Block> chunk = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Block>>(line);
                                        otherChain.AddRange(chunk);
                                    }
                                    if (otherChain.Count > blockchain.Chain.Count && blockchain.IsValidChain(otherChain))
                                    {
                                        blockchain.Chain = otherChain;
                                        Console.WriteLine($"Replaced chain with chain from {otherNodes[index]}");
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Console.WriteLine($"Error connecting to {otherNodes[index]}: {ex.Message}");
                        }
                        Thread.Sleep(5000);
                    }
                });
            }

            Thread apiThread = new Thread(() => StartApi(blockchain));
            Thread rpcThread = new Thread(() => RPCServer(blockchain));
            apiThread.Start();
            rpcThread.Start();
            if (args.Contains("-gui"))
            {
                RenderWindow window = new RenderWindow(new VideoMode(new SFML.System.Vector2u(800, 600)), "CheckmateCoin Node");

                TGUI.Gui gui = new TGUI.Gui(window);

                TGUI.Panel walletPanel = new TGUI.Panel();
                TGUI.Label balanceLabel = new TGUI.Label() { Text = "Balance: 0 CHECK" };
                if (File.Exists(Path.Combine(dataDir, "wallet.dat")))
                {
                    string walletJson = File.ReadAllText(Path.Combine(dataDir, "wallet.dat"));
                    List<string> wallet = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(walletJson);
                    int index = 0;
                    foreach (var entry in wallet)
                    {
                        byte[] privateKeyBytes = Convert.FromBase64String(entry);
                        using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                        {
                            rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                            string address = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                            TGUI.Label addressLabel = new TGUI.Label() { Text = address };
                            balanceLabel.Text = $"Balance: {blockchain.GetBalance(address)} CHECK";
                            addressLabel.Position = new TGUI.Vector2f(0, index * 40);
                            balanceLabel.Position = new TGUI.Vector2f(0, index * 40 + 20);
                            walletPanel.Add(addressLabel);
                        }
                        index++;
                    }
                }
                else
                {
                    TGUI.Button createWalletButton = new TGUI.Button();
                    createWalletButton.Text = "Create Wallet";
                    createWalletButton.OnClick += (sender, e) =>
                    {
                        using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(2048))
                        {
                            string publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                            string privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
                            List<string> wallet = new List<string>();
                            if (File.Exists(Path.Combine(dataDir, "wallet.dat")))
                            {
                                string walletJson = File.ReadAllText(Path.Combine(dataDir, "wallet.dat"));
                                wallet = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(walletJson);
                            }
                            wallet.Add(privateKey);
                            string walletJsonNew = Newtonsoft.Json.JsonConvert.SerializeObject(wallet);
                            File.WriteAllText(Path.Combine(dataDir, "wallet.dat"), walletJsonNew);
                            TGUI.Label addressLabel = new TGUI.Label() { Text = publicKey };
                            balanceLabel.Text = $"Balance: {blockchain.GetBalance(publicKey)} CHECK";
                            addressLabel.Position = new TGUI.Vector2f(0, wallet.Count * 40);
                            balanceLabel.Position = new TGUI.Vector2f(0, wallet.Count * 40 + 20);
                            walletPanel.Add(addressLabel);
                        }
                    };
                    walletPanel.Add(createWalletButton);
                }
                balanceLabel.Position = new TGUI.Vector2f(0, 40);
                walletPanel.Add(balanceLabel);

                // Send / Receive panel
                TGUI.Panel sendReceivePanel = new TGUI.Panel();
                string sendText = "Send To (Address):";
                TGUI.Label sendLabel = new TGUI.Label() { Text = sendText };
                TGUI.EditBox sendEditBox = new TGUI.EditBox();
                string amountText = "Amount:";
                TGUI.Label amountLabel = new TGUI.Label() { Text = amountText };
                TGUI.EditBox amountEditBox = new TGUI.EditBox();

                TGUI.Label statusLabel = new TGUI.Label() { Text = "" };

                TGUI.Button sendButton = new TGUI.Button() { Text = "Send" };
                TGUI.Button copyAddressButton = new TGUI.Button() { Text = "Copy My Address" };

                copyAddressButton.OnClick += (sender, e) =>
                {
                    if (File.Exists(Path.Combine(dataDir, "wallet.dat")))
                    {
                        string walletJson = File.ReadAllText(Path.Combine(dataDir, "wallet.dat"));
                        List<string> wallet = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(walletJson);
                        if (wallet.Count > 0)
                        {
                            byte[] privateKeyBytes = Convert.FromBase64String(wallet[0]);
                            using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                            {
                                rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                                string address = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                                Clipboard.Contents = address;
                                statusLabel.Text = $"Copied address {address.Substring(0, 10)}... to clipboard";
                            }
                            ;
                        }
                    }
                };


                sendButton.OnClick += (sender, e) =>
                {
                    if (File.Exists(Path.Combine(dataDir, "wallet.dat")))
                    {
                        string walletJson = File.ReadAllText(Path.Combine(dataDir, "wallet.dat"));
                        List<string> wallet = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(walletJson);
                        if (wallet.Count > 0)
                        {
                            string fromPrivateKey = wallet[0];
                            string toAddress = sendEditBox.Text;
                            int amount = int.Parse(amountEditBox.Text);
                            try
                            {
                                byte[] privateKeyBytes = Convert.FromBase64String(fromPrivateKey);
                                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                                {
                                    rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                                    string fromAddress = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                                    byte[] signatureBytes = rsa.SignData(System.Text.Encoding.UTF8.GetBytes($"checkmate{fromAddress}{toAddress}{amount}"),
                                        System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                                    if (blockchain.GetBalance(fromAddress) >= amount)
                                    {
                                        Tx tx = new Tx(fromAddress, toAddress, amount, blockchain.Chain.Count, blockchain.Chain.SelectMany(b => b.Transactions).Count() + blockchain.Mempool.Count, signatureBytes);
                                        blockchain.Mempool.Add(tx);
                                        statusLabel.Text = $"Sent {amount} CheckmateCoin to {toAddress}";
                                    }
                                    else
                                    {
                                        statusLabel.Text = $"Insufficient balance to send {amount} CheckmateCoin";
                                    }
                                }
                            }
                            catch
                            {
                                statusLabel.Text = $"Error sending CheckmateCoin";
                            }
                        }
                    }
                };
                sendLabel.Position = new TGUI.Vector2f(0, 0);
                sendEditBox.Position = new TGUI.Vector2f(0, 20);
                amountLabel.Position = new TGUI.Vector2f(0, 50);
                amountEditBox.Position = new TGUI.Vector2f(0, 70);
                statusLabel.Position = new TGUI.Vector2f(0, 100);
                sendButton.Position = new TGUI.Vector2f(0, 130);
                copyAddressButton.Position = new TGUI.Vector2f(100, 130);
                sendReceivePanel.Add(sendLabel);
                sendReceivePanel.Add(sendEditBox);
                sendReceivePanel.Add(amountLabel);
                sendReceivePanel.Add(amountEditBox);
                sendReceivePanel.Add(sendButton);
                sendReceivePanel.Add(statusLabel);
                sendReceivePanel.Add(copyAddressButton);


                // tabs
                TGUI.Tabs tabs = new TGUI.Tabs();
                tabs.Add("Wallet");
                tabs.Add("Send/Receive", false);

                tabs.OnTabSelect += (sender, e) =>
                {
                    if (tabs.Selected == "Wallet")
                    {
                        walletPanel.Visible = true;
                        sendReceivePanel.Visible = false;
                    }
                    else if (tabs.Selected == "Send/Receive")
                    {
                        walletPanel.Visible = false;
                        sendReceivePanel.Visible = true;
                    }
                };

                tabs.Position = new TGUI.Vector2f(0, 0);
                walletPanel.Position = new TGUI.Vector2f(0, 30);
                sendReceivePanel.Position = new TGUI.Vector2f(0, 30);
                gui.Add(tabs);
                sendReceivePanel.Visible = false;
                gui.Add(sendReceivePanel);
                gui.Add(walletPanel);

                Thread updateBalancesThread = new Thread(() =>
                {
                    while (window.IsOpen)
                    {
                        if (File.Exists(Path.Combine(dataDir, "wallet.dat")))
                        {
                            string walletJson = File.ReadAllText(Path.Combine(dataDir, "wallet.dat"));
                            List<string> wallet = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(walletJson);
                            int index = 0;
                            foreach (var entry in wallet)
                            {
                                byte[] privateKeyBytes = Convert.FromBase64String(entry);
                                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                                {
                                    rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
                                    string address = Convert.ToBase64String(rsa.ExportRSAPublicKey());
                                    balanceLabel.Text = $"Balance: {blockchain.GetBalance(address)} CHECK";
                                    balanceLabel.Position = new TGUI.Vector2f(0, 40);
                                }
                                index++;
                            }
                        }
                        Thread.Sleep(5000);
                    }
                });

                gui.MainLoop(TGUI.Color.White);
            }
            rpcThread.Join();
        }
    }
}
