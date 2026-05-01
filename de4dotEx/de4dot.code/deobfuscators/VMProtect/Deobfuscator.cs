using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.VMProtect {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "VMProtect";
		public const string THE_TYPE = "vmp";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = true,
				ValidNameRegex = validNameRegex.Get(),
			});
	}

	class Deobfuscator : DeobfuscatorBase {
		internal class Options : OptionsBase {
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => "VMProtect";

		List<MethodDef> stringDecrypters = new List<MethodDef>();
		VMPProxyCallFixer proxyFixer;

		public Deobfuscator(Options options)
			: base(options) {
		}

		protected override int DetectInternal() {
			int count = 0;
			foreach (var type in module.Types) {
				if (type.Name.Contains("E3A5DC83") || type.Name.Contains("BD3FC712")) // Samples from user
					count++;
				
				// VMP often creates a very large <Module> .cctor or methods
				if (type.IsGlobalModuleType) {
					foreach (var method in type.Methods) {
						if (method.Name == ".cctor" && method.HasBody && method.Body.Instructions.Count > 100)
							count++;
					}
				}
			}
			return count > 0 ? 100 : 0;
		}

		protected override void ScanForObfuscator() {
			// Find Delegate/Proxy Call Fixer
			proxyFixer = new VMPProxyCallFixer(module);
			proxyFixer.Find();
				
			// Find String Decrypters
			stringDecrypters = FindStringDecrypters().ToList();
		}

		IEnumerable<MethodDef> FindStringDecrypters() {
			var list = new List<MethodDef>();
			// VMP strings often decrypted via a method taking int and returning string
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (!method.HasBody || !method.IsStatic) continue;
					if (method.Parameters.Count == 1 && method.Parameters[0].Type.ElementType == ElementType.I4 && 
					    method.ReturnType.ElementType == ElementType.String) {
						// Check for dictionary or byte array access
						if (method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is IField f && f.Name.Contains("object_0"))) {
							list.Add(method);
						}
					}
				}
			}
			return list;
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			return stringDecrypters.Select(m => m.MDToken.ToInt32());
		}

		public override void DeobfuscateMethodBegin(Blocks blocks) {
			base.DeobfuscateMethodBegin(blocks);
			
			if (proxyFixer != null && proxyFixer.Detected)
				proxyFixer.Deobfuscate(blocks);

			// Custom VMI Stripping logic
			StripVMI(blocks);
		}

		void StripVMI(Blocks blocks) {
			var method = blocks.Method;
			if (!method.HasBody) return;

			// VMP Virtualized methods often start with a jump to the VM dispatcher
			// or have a specific pattern.
			// We try to find the original IL if it was stored as a resource or hidden in a field.
			
			var instrs = method.Body.Instructions;
			if (instrs.Count < 5) return;

			// Simple check for dispatcher call
			bool isVirtualized = false;
			foreach (var instr in instrs) {
				if (instr.OpCode == OpCodes.Call && instr.Operand is IMethod m) {
					var declaringType = m.DeclaringType;
					if (declaringType != null && (declaringType == module.GlobalType || declaringType.FullName == "<Module>")) {
						isVirtualized = true;
						break;
					}
				}
			}

			if (isVirtualized) {
				// TODO: Implement actual bytecode devirtualization here.
				// For now, we log it.
				// Console.WriteLine($"[VMP] Found virtualized method: {method.FullName}");
			}
		}
	}

	class VMPProxyCallFixer : ProxyCallFixer4 {
		Dictionary<int, IMethod> indexToMethod = new Dictionary<int, IMethod>();

		public VMPProxyCallFixer(ModuleDefMD module) : base(module) { }

		protected override object CheckCctor(TypeDef type, MethodDef cctor) {
			indexToMethod.Clear();
			FieldDef object0Field = null;

			// Step 1: Find the field 'object_0'
			foreach (var field in type.Fields) {
				if (field.Name == "object_0") {
					object0Field = field;
					break;
				}
			}

			if (object0Field == null) return null;

			// Step 2: Analyze cctor to find where indices are populated
			// Pattern: ldsfld object_0 / ldc.i4 index / ldtoken method / call GetMethodFromHandle / ... / stlem.ref
			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				if (instrs[i].OpCode == OpCodes.Ldsfld && instrs[i].Operand == object0Field) {
					int? index = null;
					if (instrs[i + 1].IsLdcI4()) index = instrs[i + 1].GetLdcI4Value();

					if (index.HasValue) {
						// Look ahead for ldtoken
						for (int j = i + 2; j < Math.Min(i + 20, instrs.Count); j++) {
							if (instrs[j].OpCode == OpCodes.Ldtoken && instrs[j].Operand is IMethod method) {
								indexToMethod[index.Value] = method;
								break;
							}
							if (instrs[j].OpCode == OpCodes.Stelem_Ref) break;
						}
					}
				}
			}

			return object0Field;
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			calledMethod = null;
			callOpcode = OpCodes.Call;

			// This is called by ProxyCallFixer4 for each field it finds.
			// But VMP uses an array, so we need to handle the array access.
			// The base ProxyCallFixer4 handles Field/Method proxies. 
			// We need to override FindProxyCall to handle 'ldsfld object_0' -> 'ldc.i4 index' -> 'ldelem.ref' -> 'call Invoke'
		}

		protected override BlockInstr FindProxyCall(DelegateInfo di, Block block, int index) {
			// di.field is 'object_0'
			var instrs = block.Instructions;
			if (index + 2 >= instrs.Count) return null;

			// Expected: 
			// index: ldsfld object_0
			// index+1: ldc.i4 <idx>
			// index+2: ldelem.ref
			// index+3: callvirt Invoke
			
			if (instrs[index + 1].IsLdcI4() && instrs[index + 2].OpCode == OpCodes.Ldelem_Ref) {
				int idx = instrs[index + 1].GetLdcI4Value();
				if (indexToMethod.TryGetValue(idx, out var method)) {
					di.methodRef = method;
					di.callOpcode = method.ResolveMethodDef()?.IsStatic == false ? OpCodes.Callvirt : OpCodes.Call;
					
					// Return the instruction index of the final 'call' (which is index+3 if it exists)
					if (index + 3 < instrs.Count && instrs[index + 3].OpCode.Code == Code.Callvirt && instrs[index + 3].Operand.ToString().Contains("Invoke")) {
						return new BlockInstr { Block = block, Index = index + 3 };
					}
				}
			}
			return null;
		}
	}
}
