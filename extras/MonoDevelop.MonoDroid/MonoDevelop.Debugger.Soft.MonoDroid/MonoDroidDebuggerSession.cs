// 
// MonoDroidDebuggerSession.cs
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
using Mono.Debugger.Soft;
using Mono.Debugging;
using Mono.Debugging.Client;
using System.Threading;
using System.Diagnostics;
using MonoDevelop.MonoDroid;
using System.IO;
using MonoDevelop.Core;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace MonoDevelop.Debugger.Soft.MonoDroid
{


	public class MonoDroidDebuggerSession : RemoteSoftDebuggerSession
	{
		System.Diagnostics.Process simProcess;
		
		protected override void OnRun (DebuggerStartInfo startInfo)
		{
			var dsi = (MonoDroidDebuggerStartInfo) startInfo;
			var cmd = dsi.ExecutionCommand;
			if (cmd.Simulator)
				StartSimulatorProcess (cmd);
			StartListening (dsi);
		}
		
		protected override string GetListenMessage (RemoteDebuggerStartInfo dsi)
		{
			var cmd = ((MonoDroidDebuggerStartInfo)dsi).ExecutionCommand;
			string message = GettextCatalog.GetString ("Waiting for debugger to connect on {0}:{1}...", dsi.Address, dsi.DebugPort);
			if (!cmd.Simulator)
				message += "\n" + GettextCatalog.GetString ("Please start the application on the device.");
			return message;
		}
		
		protected override void EndSession ()
		{
			base.EndSession ();
			EndSimProcess ();
		}

		//FIXME: hook up the app's stdin and stdout, and mtouch's stdin and stdout
		void StartSimulatorProcess (MonoDroidExecutionCommand cmd)
		{
			var psi = MonoDroidExecutionHandler.CreateMtouchSimStartInfo (cmd, false);
			psi.RedirectStandardInput = true;
			simProcess = System.Diagnostics.Process.Start (psi);
			
			simProcess.Exited += delegate {
				EndSession ();
				simProcess = null;
			};
			
			TargetExited += delegate {
				EndSimProcess ();
			};
		}
		
		void EndSimProcess ()
		{
			if (simProcess == null)
				return;
			if (!simProcess.HasExited) {
				try {
					simProcess.StandardInput.WriteLine ();
				} catch {}
			}
			GLib.Timeout.Add (10000, delegate {
				if (!simProcess.HasExited)
					simProcess.Kill ();
				return false;
			});
		}
		
		protected override void OnConnected ()
		{
			if (simProcess != null)
				MonoDroidUtility.MakeSimulatorGrabFocus ();
		}
		
		protected override void OnContinue ()
		{
			base.OnContinue ();
			if (simProcess != null)
				MonoDroidUtility.MakeSimulatorGrabFocus ();
		}
		
		protected override void OnExit ()
		{
			base.OnExit ();
			EndSimProcess ();
		}
	}
	
	class MonoDroidDebuggerStartInfo : RemoteDebuggerStartInfo
	{
		public MonoDroidExecutionCommand ExecutionCommand { get; private set; }
		
		public MonoDroidDebuggerStartInfo (IPAddress address, int debugPort, int outputPort, MonoDroidExecutionCommand cmd)
			: base (cmd.AppPath.FileNameWithoutExtension, address, debugPort, outputPort)
		{
			ExecutionCommand = cmd;
		}
	}
}
