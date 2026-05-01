/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

#if NETFRAMEWORK
using System;
using System.IO;
using System.Diagnostics;
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	public abstract class IpcAssemblyServerLoader : IAssemblyServerLoader {
		readonly string assemblyServerFilename;
		protected string ipcName;
		protected string ipcUri;
		protected AssemblyServiceType serviceType;
		string url;

		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType)
			: this(serviceType, ServerClrVersion.CLR_ANY_ANYCPU) {
		}

		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType, ServerClrVersion serverVersion) {
			this.serviceType = serviceType;
			assemblyServerFilename = GetServerName(serverVersion);
			ipcName = Utils.RandomName(15, 20);
			ipcUri = Utils.RandomName(15, 20);
			url = $"ipc://{ipcName}/{ipcUri}";
		}

		static string GetServerName(ServerClrVersion serverVersion) {
			string currentExe = Process.GetCurrentProcess().MainModule.FileName;
			string baseDir = Path.GetDirectoryName(currentExe);
			string exeName = Path.GetFileName(currentExe);

			if (serverVersion == ServerClrVersion.CLR_ANY_ANYCPU)
				serverVersion = IntPtr.Size == 4 ? ServerClrVersion.CLR_ANY_x86 : ServerClrVersion.CLR_ANY_x64;

			if (serverVersion == ServerClrVersion.CLR_ANY_x86 && IntPtr.Size == 8) {
				string x86Exe = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(exeName) + "-x86.exe");
				if (File.Exists(x86Exe)) return x86Exe;
			}
			
			return currentExe;
		}

		public void LoadServer() => LoadServer(assemblyServerFilename);
		public abstract void LoadServer(string filename);
		public IAssemblyService CreateService() => (IAssemblyService)Activator.GetObject(AssemblyService.GetType(serviceType), url);
		public abstract void Dispose();
	}
}
#else
using System;
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	public abstract class IpcAssemblyServerLoader : IAssemblyServerLoader {
		protected AssemblyServiceType serviceType;
		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType) => this.serviceType = serviceType;
		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType, ServerClrVersion serverVersion) => this.serviceType = serviceType;
		public virtual void LoadServer() => throw new NotSupportedException();
		public virtual void LoadServer(string filename) => throw new NotSupportedException();
		public virtual IAssemblyService CreateService() => throw new NotSupportedException();
		public virtual void Dispose() { }
	}
}
#endif
