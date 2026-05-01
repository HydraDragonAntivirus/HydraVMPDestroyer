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

            string workDir = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            string currentTarget = filePath;
            bool success = false;

            if (!forceDump)
            {
                // Step 1: Static Unpack
                Console.WriteLine("[INFO] Step 1: Attempting static unpack (VMP Unpacker)...");
                try
                {
                    byte[] unpackedData = VMPUnpacker.Unpack(filePath);
                    if (unpackedData != null)
                    {
                        string staticUnpackedFile = Path.Combine(workDir, fileName + "_unpacked.exe");
                        File.WriteAllBytes(staticUnpackedFile, unpackedData);
                        Console.WriteLine($"[SUCCESS] Static unpack successful: {staticUnpackedFile}");
                        currentTarget = staticUnpackedFile;
                        success = true;
                    }
                    else
                    {
                        Console.WriteLine("[WARNING] Static unpacker could not find VMP patterns.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Static unpacker failed: {ex.Message}");
                }
            }

            if (!success || forceDump)
            {
                // Step 2: Fallback to MegaDumper
                Console.WriteLine(forceDump ? "[INFO] Forced dump mode enabled." : "[INFO] Falling back to MegaDumper...");
                Console.WriteLine("[IMPORTANT] This will START the process to dump it from memory.");
                Console.Write("[?] Continue? (y/n): ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.KeyChar == 'y' || key.KeyChar == 'Y')
                {
                    try
                    {
                        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }))
                        {
                            if (process == null) throw new Exception("Failed to start process.");

                            Console.WriteLine($"[INFO] Process started (PID: {process.Id}). Waiting 4 seconds for initialization...");
                            await Task.Delay(4000);

                            var megaDumper = new Mega_Dumper.MainForm();
                            megaDumper.EnableDebuggerPrivileges();

                            string outputDir = Path.Combine(workDir, "Dumps_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                            string dumpResult = await megaDumper.DumpProcessByIdCli((uint)process.Id, outputDir);
                            Console.WriteLine($"[INFO] MegaDumper result: {dumpResult}");

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

                            if (!process.HasExited)
                            {
                                try { process.Kill(); } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] MegaDumper fallback failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[INFO] User declined memory dump.");
                }
            }

            if (success && File.Exists(currentTarget))
            {
                // Step 3: de4dotEx
                Console.WriteLine();
                Console.WriteLine($"[INFO] Step 3: Running de4dotEx on: {currentTarget}");
                try
                {
                    // Adding --strtyp delegate and other aggressive flags might help with the object_0 issue
                    string[] de4dotArgs = new string[] { 
                        currentTarget, 
                        "--un-name", "true", 
                        "--strtyp", "delegate", 
                        "--strtok", "static" 
                    };
                    de4dot.cui.Program.Main(de4dotArgs);
                    Console.WriteLine("[SUCCESS] Orchestration complete.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] de4dotEx encountered an error: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Pipeline failed. No valid assembly was produced.");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            return 0;
        }
    }
}
