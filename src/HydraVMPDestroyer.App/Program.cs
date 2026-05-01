using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MegaDumper;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace HydraVMPDestroyer.App
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("        HydraVMPDestroyer - Unified Tool");
            Console.WriteLine("   VMP Unpacker + MegaDumper + de4dotEx");
            Console.WriteLine("=================================================");

            if (args.Length < 1)
            {
                Console.WriteLine("[USAGE] HydraVMPDestroyer.App.exe <target_vmp_exe>");
                return 1;
            }

            string inputFilePath = Path.GetFullPath(args[0]);
            if (!File.Exists(inputFilePath))
            {
                Console.WriteLine($"[ERROR] File not found: {inputFilePath}");
                return 1;
            }

            Console.WriteLine($"[INFO] Runtime Architecture: {(IntPtr.Size == 8 ? "x64" : "x86")}");
            Console.WriteLine("[!] Note: Specifically for .NET VMProtect executables.");

            string outputDir = Path.GetDirectoryName(inputFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dumpPath = Path.Combine(outputDir, $"Dumps_{timestamp}");
            
            Directory.CreateDirectory(dumpPath);

            Console.WriteLine("\n[INFO] Starting Dynamic Analysis Mode...");
            
            ProcessStartInfo startInfo = new ProcessStartInfo(inputFilePath);
            startInfo.WorkingDirectory = outputDir;
            
            Process process = Process.Start(startInfo);
            Console.WriteLine($"[INFO] Process started (PID: {process.Id}). Waiting 5 seconds for initialization...");
            await Task.Delay(5000);

            bool success = false;
            string currentTarget = "";

            try
            {
                Mega_Dumper.MainForm megaDumper = new Mega_Dumper.MainForm();
                string dumpResult = await megaDumper.DumpProcessByIdCli((uint)process.Id, dumpPath);
                Console.WriteLine($"[INFO] MegaDumper finished: {dumpResult}");
                
                string dumpsDir = Path.Combine(dumpPath, "dumps");
                if (Directory.Exists(dumpsDir))
                {
                    // Recursive search for the primary assembly
                    var files = Directory.GetFiles(dumpsDir, "*.exe", SearchOption.AllDirectories);
                    
                    // Prioritize files named 'vdump' or the largest exe
                    string found = files.FirstOrDefault(f => f.ToLower().Contains("vdump")) ?? 
                                   files.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

                    if (!string.IsNullOrEmpty(found))
                    {
                        currentTarget = found;
                        success = true;
                    }
                }

                if (!process.HasExited) { try { process.Kill(); } catch { } }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Dumping failed: {ex.Message}");
            }

            if (success && File.Exists(currentTarget))
            {
                // Step 2: Restore Original Filename (CRITICAL for VMP)
                string originalName = Path.GetFileName(inputFilePath);
                string renamedFile = Path.Combine(Path.GetDirectoryName(currentTarget), originalName);
                try 
                {
                    if (File.Exists(renamedFile)) File.Delete(renamedFile);
                    File.Move(currentTarget, renamedFile);
                    currentTarget = renamedFile;
                    Console.WriteLine($"[SUCCESS] Renamed dump to: {originalName}");
                } catch { }

                // VMP ANTI-TAMPER FIX: Do NOT normalize the assembly with dnlib here!
                // VMP's .cctor hashes the PE headers. Any structural changes (even fixing alignments)
                // triggers the Anti-Tamper, causing the dictionary to remain empty (KeyNotFoundException).
                // We pass the raw dump directly to de4dotEx.

                // Step 3: Deobfuscate
                Console.WriteLine("\n[INFO] Running de4dotEx...");
                
                string de4dotDllPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\de4dotEx\Debug\net8.0\de4dot.cui.dll"));
                
                if (!File.Exists(de4dotDllPath))
                {
                    Console.WriteLine($"[ERROR] Could not find de4dotEx at: {de4dotDllPath}");
                }
                else
                {
                    string de4dotArgs = $"\"{de4dotDllPath}\" --un-name true --strtyp emulate --strtok true --delegate-to-method true --proxy-calls true --file \"{currentTarget}\"";
                    
                    try
                    {
                        var de4dotProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = de4dotArgs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });
                        Console.WriteLine(de4dotProc.StandardOutput.ReadToEnd());
                        de4dotProc.WaitForExit();
                    }
                    catch (Exception ex) { Console.WriteLine($"[ERROR] de4dotEx error: {ex.Message}"); }
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Pipeline aborted: No valid dump found.");
            }

            Console.WriteLine("\n[SUCCESS] Pipeline finished. Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(0);
            return 0;
        }

        private static void NormalizeAssembly(string filePath)
        {
            Console.WriteLine($"[INFO] Normalizing with dnlib: {Path.GetFileName(filePath)}");
            try
            {
                using (var module = ModuleDefMD.Load(filePath))
                {
                    var options = new ModuleWriterOptions(module);
                    options.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;
                    options.Logger = DummyLogger.NoThrowInstance;
                    
                    // VMP Fix: We don't touch EntryPoint here, just let dnlib fix the RVAs/Layout
                    string temp = filePath + ".tmp";
                    module.Write(temp, options);
                    File.Delete(filePath);
                    File.Move(temp, filePath);
                }
                Console.WriteLine("[SUCCESS] Normalization complete.");
            }
            catch (Exception ex) { Console.WriteLine($"[WARNING] Normalization skipped: {ex.Message}"); }
        }
    }
}
