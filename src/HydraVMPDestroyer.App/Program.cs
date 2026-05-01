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
            Console.WriteLine($"[INFO] Runtime Architecture: {(IntPtr.Size == 4 ? "x86" : "x64")}");
            Console.WriteLine("[!] Note: Specifically for .NET VMProtect executables.");
            Console.WriteLine();

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: HydraVMPDestroyer.App <file_path> [--force-dump]");
                return 1;
            }

            string filePath = Path.GetFullPath(args[0]);
            bool forceDump = args.Contains("--force-dump");

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File '{filePath}' not found.");
                return 1;
            }

            string workDir = Path.GetDirectoryName(filePath) ?? "";
            string currentTarget = filePath;
            bool success = false;

            Console.WriteLine("[INFO] Direct Dynamic Analysis Mode: Using MegaDumper...");
            try
            {
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }))
                {
                    if (process == null) throw new Exception("Failed to start process.");

                    Console.WriteLine($"[INFO] Process started (PID: {process.Id}). Waiting 5 seconds for initialization...");
                    await Task.Delay(5000);

                    var megaDumper = new Mega_Dumper.MainForm();
                    megaDumper.EnableDebuggerPrivileges();

                    string outputDir = Path.Combine(workDir, "Dumps_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Console.WriteLine($"[INFO] Dumping process to: {outputDir}");
                    
                    string dumpResult = await megaDumper.DumpProcessByIdCli((uint)process.Id, outputDir);
                    
                    string dumpsDir = Path.Combine(outputDir, "dumps");
                    if (Directory.Exists(dumpsDir))
                    {
                        var files = Directory.GetFiles(dumpsDir, "*.exe");
                        if (files.Length > 0)
                        {
                            currentTarget = files.OrderByDescending(f => new FileInfo(f).Length).First();
                            Console.WriteLine($"[SUCCESS] Memory dump obtained: {currentTarget}");
                            success = true;
                        }
                    }

                    if (!process.HasExited) { try { process.Kill(); } catch { } }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] MegaDumper failed: {ex.Message}");
            }

            if (success && File.Exists(currentTarget))
            {
                // Step 2: de4dotEx
                Console.WriteLine();
                Console.WriteLine($"[INFO] Running de4dotEx on memory dump: {currentTarget}");
                
                string[] de4dotArgs = new string[] { 
                    currentTarget, 
                    "--un-name", "true", 
                    "--strtyp", "delegate", 
                    "--strtok", "static",
                    "--strtyp", "emulate"
                };

                int exitCode = de4dot.cui.Program.Main(de4dotArgs);
                if (exitCode == 0)
                {
                    Console.WriteLine("[SUCCESS] Deobfuscation complete.");
                }
                else
                {
                    Console.WriteLine("[ERROR] de4dotEx failed to process the memory dump.");
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Failed to obtain a valid memory dump.");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            return 0;
        }
    }
}
