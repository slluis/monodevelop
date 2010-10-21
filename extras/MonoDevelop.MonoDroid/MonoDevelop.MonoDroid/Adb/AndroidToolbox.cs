// 
// AndroidToolbox.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2010 Novell, Inc.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
using MonoDevelop.Core.ProgressMonitoring;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace MonoDevelop.MonoDroid
{
	public class AndroidToolbox
	{
		FilePath androidToolsPath, javaBinPath;
		string pathOverride;
		
		public AndroidToolbox (FilePath androidToolsPath, FilePath javaBinPath)
		{
			this.androidToolsPath = androidToolsPath;
			this.javaBinPath = javaBinPath;
			
			pathOverride = Environment.GetEnvironmentVariable ("PATH");
			if (string.IsNullOrEmpty (pathOverride))
				pathOverride = javaBinPath;
			else
				pathOverride = javaBinPath + Path.PathSeparator + pathOverride;
		}
		
		public string AdbExe {
			get {
				return androidToolsPath.Combine (PropertyService.IsWindows? "adb.exe" : "adb");
			}
		}
		
		public string AndroidExe {
			get {
				return androidToolsPath.Combine (PropertyService.IsWindows? "android.bat" : "android");
			}
		}
		
		public string EmulatorExe {
			get {
				return androidToolsPath.Combine (PropertyService.IsWindows? "emulator.exe" : "emulator");
			}
		}
		
		public string JarsignerExe {
			get {
				return javaBinPath.Combine (PropertyService.IsWindows? "jarsigner.exe" : "jarsigner");
			}
		}
		
		public string KeytoolExe {
			get {
				return javaBinPath.Combine (PropertyService.IsWindows? "keytool.exe" : "keytool");
			}
		}
		
		public IProcessAsyncOperation SignPackage (AndroidSigningOptions options, string unsignedApk,
			string signedApk, TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format (
				"-keystore \"{0}\" -storepass \"{1}\" -keypass \"{2}\" -signedjar \"{3}\" \"{4}\" \"{5}\"",
				options.KeyStore, options.StorePass, options.KeyPass , signedApk, unsignedApk, options.KeyAlias);
			return StartProcess (JarsignerExe, args, outputLog, errorLog);
		}
		
		//string dname = "CN=Android Debug,O=Android,C=US";
		public IProcessAsyncOperation Genkeypair (AndroidSigningOptions options, string dname,
			TextWriter outputLog, TextWriter errorLog)
		{
			
			var args = string.Format (
				"-genkeypair -alias \"{0}\" -dname \"{1}\" -storepass \"{3}\" -keypass \"{2}\" -keystore \"{4}\"",
				options.KeyAlias, dname, options.StorePass, options.KeyPass, options.KeyStore);
			return StartProcess (KeytoolExe, args, outputLog, errorLog);
		}
		
		ProcessWrapper StartProcess (string name, string args, TextWriter outputLog, TextWriter errorLog)
		{
			var psi = new ProcessStartInfo (name, args) {
				UseShellExecute = false,
			};
			if (outputLog != null)
				psi.RedirectStandardOutput = true;
			if (errorLog != null)
				psi.RedirectStandardError = true;
			psi.EnvironmentVariables["PATH"] = pathOverride;
			return Runtime.ProcessService.StartProcess (psi, outputLog, outputLog, null);
		}
		
		ProcessWrapper StartProcess (string name, string args, ProcessEventHandler outputLog, ProcessEventHandler errorLog)
		{
			var psi = new ProcessStartInfo (name, args) {
				UseShellExecute = false,
			};
			if (outputLog != null)
				psi.RedirectStandardOutput = true;
			if (errorLog != null)
				psi.RedirectStandardError = true;
			psi.EnvironmentVariables["PATH"] = pathOverride;
			return Runtime.ProcessService.StartProcess (psi, outputLog, outputLog, null);
		}
		
		public IProcessAsyncOperation EnsureServerRunning (TextWriter outputLog, TextWriter errorLog)
		{
			return StartProcess (AdbExe, "start-server", outputLog, errorLog);
		}
		
		public GetDevicesOperation GetDevices (TextWriter errorLog)
		{
			var output = new StringWriter ();
			return new GetDevicesOperation (StartProcess (AdbExe, "devices", output, errorLog), output);
		}
		
		public GetVirtualDevicesOperation GetAllVirtualDevices (TextWriter errorLog)
		{
			var output = new StringWriter ();
			return new GetVirtualDevicesOperation (StartProcess (AndroidExe, "list avd", output, errorLog), output);
		}
		
		public StartAvdOperation StartAvd (AndroidVirtualDevice avd)
		{
			var error = new StringWriter ();
			string args = string.Format ("-partition-size 512 -avd '{0}'", avd.Name);
			var process = StartProcess (EmulatorExe, args, null, error);
			return new StartAvdOperation (process, error);
		}
		
		public ProcessWrapper StartAvdManager ()
		{
			//FIXME: don't create multiple instances. if it's running, focus it.
			return Runtime.ProcessService.StartProcess (AndroidExe, "", null, (TextWriter)null, null, null);
		}
		
		public IProcessAsyncOperation PushFile (AndroidDevice device, string source, string destination,
			TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' push '{1} '{2}'", device.ID, source, destination);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}

		public IProcessAsyncOperation WaitForDevice (AndroidDevice device, TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' wait-for-device", device.ID);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}

		public IProcessAsyncOperation PullFile (AndroidDevice device, string source, string destination,
			TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' pull '{1} '{2}'", device.ID, source, destination);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}

		public InstallPackageOperation Install (AndroidDevice device, string package, TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' install '{1}'", device.ID, package);
			var errorCapture = new StringWriter ();
			var errorWriter = TeeTextWriter.ForNonNull (errorCapture, errorCapture);
			return new InstallPackageOperation (StartProcess (AdbExe, args, outputLog, errorWriter), errorCapture);
		}

		public IProcessAsyncOperation Uninstall (AndroidDevice device, string package, TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' uninstall '{1}'", device.ID, package);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}
		
		//monoRuntimeArgs: debug={0}:{1}:{2}", DebuggerHost, SdbPort, StdoutPort
		public IProcessAsyncOperation StartActivity (AndroidDevice device, string activity, string monoRuntimeArgs,
			TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' shell am start -a android.intent.action.MAIN -n '{1}'",
				device.ID, activity);
			
			if (!string.IsNullOrEmpty (monoRuntimeArgs)) {
				args = args + " -e mono:runtime-args " + monoRuntimeArgs;
			}
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}
		
		public IProcessAsyncOperation StartActivity (AndroidDevice device, string activity, string monoRuntimeArgs,
			ProcessEventHandler outputLog, ProcessEventHandler errorLog)
		{
			var args = string.Format ("-s '{0}' shell am start -a android.intent.action.MAIN -n '{1}'",
				device.ID, activity);
			
			if (!string.IsNullOrEmpty (monoRuntimeArgs)) {
				args = args + " -e mono:runtime-args " + monoRuntimeArgs;
			}
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}

		public IProcessAsyncOperation ForwardPort (AndroidDevice device, int devicePort, int localPort,
			TextWriter outputLog, TextWriter errorLog)
		{
			var args = string.Format ("-s '{0}' forward tcp:{1} tcp:{2}", device.ID, localPort, devicePort);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}
		
		public GetPackagesOperation GetInstalledPackagesOnDevice (AndroidDevice device, TextWriter errorWriter)
		{
			var output = new StringWriter ();
			var args = string.Format ("-s '{0}' shell pm list packages", device.ID);
			return new GetPackagesOperation (StartProcess (AdbExe, args, output, errorWriter), output);
		}
		
		public ProcessWrapper LogCat (AndroidDevice device, ProcessEventHandler outputLog,
			ProcessEventHandler errorLog)
		{
			var args = string.Format ("-s '{0}' logcat", device.ID);
			return StartProcess (AdbExe, args, outputLog, errorLog);
		}

		public bool IsSharedRuntimeInstalled (List<string> packages)
		{
			return packages.Contains ("package:com.novell.monodroid.runtimeservice");
		}
		
		public abstract class AdbParseOperation<T> : WrapperOperation
		{
			IProcessAsyncOperation process;
			StringWriter output;
			T result;
			
			public AdbParseOperation (IProcessAsyncOperation process, StringWriter output)
			{
				this.process = process;
				this.output = output;
			}
			
			protected override IAsyncOperation Wrapped {
				get { return process; }
			}
			
			protected abstract T Parse (string result);
			
			public T Result {
				get {
					if (result == null)
						result = Parse (output.ToString ());
					return result;
				}
			}
		}
	
		public class GetPackagesOperation : AdbParseOperation<List<string>>
		{
			public GetPackagesOperation (IProcessAsyncOperation process, StringWriter output)
				: base (process, output)
			{
			}
			
			protected override List<string> Parse (string output)
			{
				var sr = new StringReader (output);
				var list = new List<string> ();
				
				string s;
				while ((s = sr.ReadLine ()) != null) {
					s.Trim ();
					if (!string.IsNullOrEmpty (s))
						list.Add (s);
				}
				return list;
			}
		}
		
		public class GetVirtualDevicesOperation : AdbParseOperation<List<AndroidVirtualDevice>>
		{
			public GetVirtualDevicesOperation (IProcessAsyncOperation process, StringWriter output)
				: base (process, output)
			{
			}
			
			protected override List<AndroidVirtualDevice> Parse (string output)
			{
				var devices = new List<AndroidVirtualDevice> ();
	
				var sr = new StringReader (output);
				AndroidVirtualDevice current_device = null;
	
				string s;
	
				while ((s = sr.ReadLine ()) != null) {
					s = s.Trim ();
	
					if (s.Length == 0)
						continue;
	
					if (s.StartsWith ("Available") || s.StartsWith ("------")) {
						if (current_device != null)
							devices.Add (current_device);
	
						continue;
					}
	
					if (s.StartsWith ("Name:"))
						current_device = new AndroidVirtualDevice ();
	
					string[] data = s.Split (':');
	
					// Check for a multi-line property, and ignore it
					if (data.Length > 1 && current_device != null)
						current_device.Properties[data[0].Trim ()] = data[1].Trim ();
				}
	
				if (current_device != null)
					devices.Add (current_device);
	
				return devices;
			}
		}
		
		public class GetDevicesOperation : AdbParseOperation<List<AndroidDevice>>
		{
			public GetDevicesOperation (IProcessAsyncOperation process, StringWriter output)
				: base (process, output)
			{
			}
			
			protected override List<AndroidDevice> Parse (string output)
			{
				var devices = new List<AndroidDevice> ();
				
				using (var sr = new StringReader (output)) {
					string s;
		
					while ((s = sr.ReadLine ()) != null) {
						s = s.Trim ();
						
						if (s.Length == 0 || s == "List of devices attached")
							continue;
		
						string[] data = s.Split ('\t');
						devices.Add (new AndroidDevice (data[0].Trim (), data[1].Trim ()));
					}
				}
				return devices;
			}
		}
		
		public class StartAvdOperation : WrapperOperation
		{
			IProcessAsyncOperation process;
			StringWriter error;
			
			public StartAvdOperation (IProcessAsyncOperation process, StringWriter error)
			{
				this.process = process;
				this.error = error;
			}
			
			protected override IAsyncOperation Wrapped {
				get { return process; }
			}
			
			public override bool Success {
				get {
					return base.Success || error.ToString ().Contains ("used by another emulator");
				}
			}
			
			public string ErrorText {
				get { return error.ToString (); }
			}
		}
		
		public class InstallPackageOperation : WrapperOperation
		{
			IProcessAsyncOperation process;
			StringWriter error;
			
			public InstallPackageOperation (IProcessAsyncOperation process, StringWriter error)
			{
				this.process = process;
				this.error = error;
			}
			
			protected override IAsyncOperation Wrapped {
				get { return process; }
			}
			
			public override bool Success {
				get {
					// adb exit code is 0 with some errors
					return base.Success && !error.ToString ().Contains ("[INSTALL_FAILED");
				}
			}
			
			//FIXME: use this
			public string FriendlyError {
				get {
					string err = error.ToString ();
					if (err.Contains ("[INSTALL_FAILED_INSUFFICIENT_STORAGE]"))
						return "There is not enough storage space on the device to store the package.";
					if (err.Contains ("[INSTALL_FAILED_ALREADY_EXISTS]"))
						return "The package already exists on the device.";
					return null;
				}
			}
		}
	}
	
	public class AndroidSigningOptions
	{
		public string KeyStore { get; set; }
		public string KeyAlias { get; set; }
		public string KeyPass { get; set; }
		public string StorePass { get; set; }
	}
	
	public class AndroidDevice
	{
		public string ID { get; set; }
		public string State { get; set; }

		public AndroidDevice () {}

		public AndroidDevice (string id, string state)
		{
			ID = id;
			State = state;
		}

		public bool IsEmulator {
			get { return ID.ToLowerInvariant ().StartsWith ("emulator"); }
		}

		public override string ToString ()
		{
			return string.Format ("Device: {0} [{1}]", ID, State);
		}
		
		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;
			if (ReferenceEquals (this, obj))
				return true;
			var other = obj as MonoDevelop.MonoDroid.AndroidDevice;
			return other != null && ID == other.ID && State == other.State && IsEmulator == other.IsEmulator;
		}
		
		public override int GetHashCode ()
		{
			unchecked {
				return (ID != null ? ID.GetHashCode () : 0) 
					^ (State != null ? State.GetHashCode () : 0)
					^ IsEmulator.GetHashCode ();
			}
		}
	}
	
	public class AndroidVirtualDevice
	{
		public Dictionary<string, string> Properties { get; private set; }

		public AndroidVirtualDevice ()
		{
			Properties = new Dictionary<string,string> ();
		}

		#region Convenience Properties
		public string Name { get { return Properties["Name"]; } }
		public string Path { get { return Properties["Path"]; } }
		public string Target { get { return Properties["Target"]; } }
		public string Skin { get { return Properties["Skin"]; } }
		#endregion

		public override string ToString ()
		{
			return string.Format ("Virtual Device: {0} - {2} [{1}]", Name, Path, Target);
		}
		
		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;
			if (ReferenceEquals (this, obj))
				return true;
			var other = obj as MonoDevelop.MonoDroid.AndroidVirtualDevice;
			return other != null && DictEqual (Properties, other.Properties);
		}
		
		internal static bool DictEqual<K,V> (Dictionary<K,V> a, Dictionary<K,V> b)
		{
			if (a == null && b == null)
				return true;
			if (a == null || b == null)
				return false;
			if (a.Count != b.Count)
				return false;
			foreach (var kv in a) {
				V otherVal;
				if (!b.TryGetValue (kv.Key, out otherVal) || !object.Equals (otherVal, kv.Value))
					return false;
			}
			return true;
		}
		
		static int GetStringDictHash (Dictionary<string, string> a)
		{
			int val = 0;
			if (a != null) {
				foreach (var kvp in a) {
					unchecked {
						val = val ^ kvp.Key.GetHashCode () ^ (kvp.Value == null? 0 : kvp.Value.GetHashCode ());
					}
				}
			}
			return val;
		}
	
		public override int GetHashCode ()
		{
			return GetStringDictHash (Properties);
		}
	}
	
	public abstract class WrapperOperation : IAsyncOperation, IDisposable
	{
		bool disposed;
		
		protected abstract IAsyncOperation Wrapped { get; }
		
		public virtual event OperationHandler Completed {
			add { Wrapped.Completed += value; }
			remove { Wrapped.Completed -= value; }
		}
		
		public virtual void Cancel ()
		{
			Wrapped.Cancel ();
		}
		
		public virtual void WaitForCompleted ()
		{
			Wrapped.WaitForCompleted ();
		}
		
		public virtual bool IsCompleted {
			get { return Wrapped.IsCompleted; }
		}
		
		public virtual bool Success {
			get { return Wrapped.Success; }
		}
		
		public virtual bool SuccessWithWarnings {
			get { return Wrapped.SuccessWithWarnings; }
		}
		
		public void Dispose ()
		{
			if (disposed)
				return;
			disposed = true;
			
			Dispose (true);
			GC.SuppressFinalize (this);
		}
		
		protected virtual void Dispose (bool disposing)
		{
			if (!disposing)
				return;
			
			if (Wrapped is IDisposable)
				((IDisposable)Wrapped).Dispose ();
		}
		
		~WrapperOperation ()
		{
			
		}
	}
	
	class TeeTextWriter : TextWriter
	{
		TextWriter [] writers;
		
		public TeeTextWriter (params TextWriter [] writers)
		{
			this.writers = writers;
			foreach (var w in writers)
				if (w.Encoding != writers[0].Encoding)
					throw new ArgumentException ("writers must have same encoding");
		}
		
		public override void Write (string value)
		{
			foreach (var writer in writers)
				writer.Write (value);
		}
		
		public static TextWriter ForNonNull (params TextWriter [] writers)
		{
			var nonNull = writers.Where (w => w != null).ToArray ();
			if (nonNull.Length == 0)
				return null;
			if (nonNull.Length == 1)
				return nonNull[0];
			return new TeeTextWriter (nonNull);
		}
		
		public override System.Text.Encoding Encoding {
			get {
				return writers[0].Encoding;
			}
		}
	}
}

