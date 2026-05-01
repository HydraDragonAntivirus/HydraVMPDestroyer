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
                    Console.WriteLine($"[INFO] MegaDumper finished with result: {dumpResult}");
                    
                    string dumpsDir = Path.Combine(outputDir, "dumps");
                    if (Directory.Exists(dumpsDir))
                    {
                        // Find the most likely dump target
                        string foundTarget = "";
                        var files = Directory.GetFiles(dumpsDir, "*.*", SearchOption.AllDirectories);
                        
                        // Prioritize vdump_*.exe files (virtual dumps) in any directory
                        foreach (var file in files) {
                            if (file.ToLower().Contains("vdump") && file.ToLower().EndsWith(".exe")) {
                                foundTarget = file;
                                break;
                            }
                        }

                        // Fallback to any .exe in the dumps folder if no vdump found
                        if (string.IsNullOrEmpty(foundTarget)) {
                            foreach (var file in files) {
                                if (file.ToLower().EndsWith(".exe")) {
                                    foundTarget = file;
                                    break;
                                }
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(foundTarget))
                        {
                            currentTarget = foundTarget;
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
                // Step 1.5: Fix Memory Dump Alignment
                try
                {
                    FixMemoryDumpAlignment(currentTarget);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Failed to fix dump alignment: {ex.Message}");
                }

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

            Console.WriteLine("\n[SUCCESS] Pipeline finished. Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(0);
            return 0;
        }

        private static void FixMemoryDumpAlignment(string filePath)
        {
            Console.WriteLine($"[INFO] Normalizing assembly with dnlib: {Path.GetFileName(filePath)}");
            
            try
            {
                // Load with dnlib (it's very tolerant of bad headers)
                using (var module = dnlib.DotNet.ModuleDefMD.Load(filePath))
                {
                    string tempPath = filePath + ".tmp";
                    
                    // We rewrite the file. dnlib will automatically fix:
                    // 1. Section alignments
                    // 2. File alignment
                    // 3. Metadata pointers
                    // 4. Invalid EntryPoint (if any)
                    
                    var writerOptions = new dnlib.DotNet.Writer.ModuleWriterOptions(module);
                    writerOptions.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                    writerOptions.Logger = dnlib.DotNet.DummyLogger.NoThrowInstance;
                    
                    module.Write(tempPath, writerOptions);
                    
                    // Replace original with normalized version
                    File.Delete(filePath);
                    File.Move(tempPath, filePath);
                }
                Console.WriteLine("[SUCCESS] Assembly normalized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] dnlib normalization failed: {ex.Message}. Attempting manual fallback...");
                ManualFixAlignment(filePath);
            }
        }

        private static void ManualFixAlignment(string filePath)
        {
            byte[] data = File.ReadAllBytes(filePath);
            if (data.Length < 0x200) return;
            // ... (rest of the manual logic if needed, but dnlib is preferred)
            // For now, I'll keep the previous manual logic as a fallback
            InternalManualFix(data, filePath);
        }

        private static void InternalManualFix(byte[] data, string filePath)
        {
            int peHeaderOffset = BitConverter.ToInt32(data, 0x3C);
            int optionalHeaderOffset = peHeaderOffset + 4 + 20;
            
            bool is64Bit = BitConverter.ToUInt16(data, optionalHeaderOffset) == 0x20B;
            Console.WriteLine($"[INFO] Manual Fix - Detected Dump Bitness: {(is64Bit ? "x64" : "x86 (32-bit)")}");

            int sectionAlignment = BitConverter.ToInt32(data, optionalHeaderOffset + 32);
            
            // Set FileAlignment = SectionAlignment
            Buffer.BlockCopy(BitConverter.GetBytes(sectionAlignment), 0, data, optionalHeaderOffset + 36, 4);
            // Zero out EntryPoint
            Buffer.BlockCopy(new byte[4], 0, data, optionalHeaderOffset + 16, 4);

            int numberOfSections = BitConverter.ToUInt16(data, peHeaderOffset + 4 + 2);
            int sizeOfOptionalHeader = BitConverter.ToUInt16(data, peHeaderOffset + 4 + 16);
            int sectionTableOffset = peHeaderOffset + 4 + 20 + sizeOfOptionalHeader;

            for (int i = 0; i < numberOfSections; i++)
            {
                int sectionOffset = sectionTableOffset + (i * 40);
                int virtualAddress = BitConverter.ToInt32(data, sectionOffset + 12);
                int virtualSize = BitConverter.ToInt32(data, sectionOffset + 8);
                Buffer.BlockCopy(BitConverter.GetBytes(virtualAddress), 0, data, sectionOffset + 20, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(virtualSize), 0, data, sectionOffset + 16, 4);
            }
            File.WriteAllBytes(filePath, data);
        }
    }
}
