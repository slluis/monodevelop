// 
// SoftDebuggerStartInfo.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2010 Novell, Inc. (http://www.novell.com)
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
using Mono.Debugging.Client;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Net;

namespace Mono.Debugging.Soft
{
	public class SoftDebuggerStartInfo : DebuggerStartInfo
	{
		public SoftDebuggerStartInfo (string monoRuntimePrefix, Dictionary<string,string> monoRuntimeEnvironmentVariables)
		{
			this.MonoRuntimePrefix = monoRuntimePrefix;
			this.MonoRuntimeEnvironmentVariables = monoRuntimeEnvironmentVariables;
		}
		
		public string MonoRuntimePrefix { get; private set; }
		public Dictionary<string,string> MonoRuntimeEnvironmentVariables { get; private set; }
		
		public List<AssemblyName> UserAssemblyNames { get; set; }
		
		/// <summary>
		/// The session will output this to the debug log as soon as it starts. It can be used to log warnings from
		/// creating the SoftDebuggerStartInfo
		/// </summary>
		public string LogMessage { get; set; }
		
		public Mono.Debugger.Soft.LaunchOptions.TargetProcessLauncher ExternalConsoleLauncher;
	}
	
	public class RemoteSoftDebuggerStartInfo : DebuggerStartInfo
	{
		public IPAddress Address { get; private set; }
		public int DebugPort { get; private set; }
		public int OutputPort { get; private set; }
		
		public bool RedirectOutput { get { return OutputPort > 0; } }
		
		public string AppName { get; set; }
		public List<AssemblyName> UserAssemblyNames { get; set; }
		
		/// <summary>
		/// The session will output this to the debug log as soon as it starts. It can be used to log warnings from
		/// creating the SoftDebuggerStartInfo
		/// </summary>
		public string LogMessage { get; set; }
		
		public RemoteSoftDebuggerStartInfo (string appName, IPAddress address, int debugPort)
			: this (appName, address, debugPort, 0) {}
		
		public RemoteSoftDebuggerStartInfo (string appName, IPAddress address, int debugPort, int outputPort)
		{
			this.AppName = appName;
			this.Address = address;
			this.DebugPort = debugPort;
			this.OutputPort = outputPort;
		}
	}
}

