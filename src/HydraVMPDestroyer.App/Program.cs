using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using HydraVMPDestroyer.App.Unpacker;
using Mega_Dumper;
using de4dot.cui;

namespace HydraVMPDestroyer.App
{
    class Program
    {
        [STAThread]
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("        HydraVMPDestroyer - Unified Tool");
            Console.WriteLine("   VMP Unpacker + MegaDumper + de4dotEx");
            Console.WriteLine("=================================================");
            Console.WriteLine("[!] Note: Specifically for .NET VMProtect executables.");
            Console.WriteLine();

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: HydraVMPDestroyer.App <file_path>");
                return 1;
            }

            string filePath = Path.GetFullPath(args[0]);
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File '{filePath}' not found.");
                return 1;
            }

            Console.WriteLine($"[INFO] Target: {filePath}");

            // Step 1: Static Unpack
            Console.WriteLine("[INFO] Attempting static unpack (VMP Unpacker)...");
            byte[] unpackedData = null;
            try {
                unpackedData = VMPUnpacker.Unpack(filePath);
            } catch (Exception ex) {
                Console.WriteLine($"[ERROR] Static unpacker error: {ex.Message}");
            }

            string dumpedFile = null;
            if (unpackedData != null)
            {
                dumpedFile = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_unpacked.exe");
                File.WriteAllBytes(dumpedFile, unpackedData);
                Console.WriteLine($"[SUCCESS] Static unpack successful: {dumpedFile}");
            }
            else
            {
                Console.WriteLine("[WARNING] Static unpack failed or RVA patterns not found. Falling back to MegaDumper...");
                
                // Step 2: Fallback to MegaDumper
                Console.WriteLine("[IMPORTANT] Falling back to MegaDumper. This will START the process to dump it from memory.");
                Console.Write("[?] Continue? (y/n): ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.KeyChar != 'y' && key.KeyChar != 'Y')
                {
                    Console.WriteLine("[INFO] Aborted by user.");
                    return 0;
                }

                try {
                    using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }))
                    {
                        if (process == null) throw new Exception("Failed to start process.");
                        
                        Console.WriteLine($"[INFO] Process started (PID: {process.Id}). Waiting 3 seconds for initialization...");
                        await Task.Delay(3000); 

                        var megaDumper = new Mega_Dumper.MainForm();
                        megaDumper.EnableDebuggerPrivileges();
                        
                        string outputDir = Path.Combine(Path.GetDirectoryName(filePath), "Dumps_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                        Console.WriteLine($"[INFO] Dumping to: {outputDir}");
                        
                        string result = await megaDumper.DumpProcessByIdCli((uint)process.Id, outputDir);
                        Console.WriteLine($"[INFO] MegaDumper result: {result}");
                        
                        // Look for dumped .NET assembly
                        string dumpsDir = Path.Combine(outputDir, "dumps");
                        if (Directory.Exists(dumpsDir))
                        {
                            var files = Directory.GetFiles(dumpsDir, "*.exe");
                            if (files.Length > 0)
                                dumpedFile = files.OrderByDescending(f => new FileInfo(f).Length).First(); // Pick largest or first
                        }
                        
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch { }
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"[ERROR] Fallback failed: {ex.Message}");
                }
            }

            if (dumpedFile != null && File.Exists(dumpedFile))
            {
                // Step 3: de4dotEx
                Console.WriteLine();
                Console.WriteLine($"[INFO] Running de4dotEx on: {dumpedFile}");
                try {
                    string[] de4dotArgs = new string[] { dumpedFile };
                    de4dot.cui.Program.Main(de4dotArgs);
                } catch (Exception ex) {
                    Console.WriteLine($"[ERROR] de4dotEx failed: {ex.Message}");
                }
                Console.WriteLine("[SUCCESS] Processing complete.");
            }
            else
            {
                Console.WriteLine("[ERROR] Could not obtain a dumped file for deobfuscation.");
            }

            return 0;
        }
    }
}
