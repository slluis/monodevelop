//
// ResolveResult.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using MonoDevelop.Projects.Dom.Parser;
using System.Diagnostics;
using System.Linq;

namespace MonoDevelop.Projects.Dom
{
	public abstract class ResolveResult
	{
		public IType CallingType {
			get;
			set;
		}

		public virtual IReturnType ResolvedType {
			get;
			set;
		}
		public virtual IReturnType UnresolvedType {
			get;
			set;
		}

		public IMember CallingMember {
			get;
			set;
		}

		public bool StaticResolve {
			get;
			set;
		}

		public ExpressionResult ResolvedExpression {
			get;
			set;
		}
		
		public virtual IEnumerable<ResolveResult> ResolveResults {
			get { yield return this; }
		}
		
		List<string> resolveErrors = new List<string> ();
		public List<string> ResolveErrors {
			get {
				return resolveErrors;
			}
		}
		
		public ResolveResult () : this (false)
		{
		}
		
		public ResolveResult (bool staticResolve)
		{
			this.StaticResolve = staticResolve;
		}
		
		public abstract IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember);
	}
	
	public class AggregatedResolveResult : ResolveResult
	{
		List<ResolveResult> resolveResults = new List<ResolveResult> ();
		
		public ResolveResult PrimaryResult {
			get {
				if (resolveResults.Count == 0)
					return null;
				return resolveResults[0];
			}
		}
		
		public override IReturnType UnresolvedType {
			get {
				if (PrimaryResult == null)
					return base.UnresolvedType;
				return PrimaryResult.UnresolvedType;
			}
		}
		public override IEnumerable<ResolveResult> ResolveResults {
			get { return this.resolveResults; }
		}
		
		public override IReturnType ResolvedType {
			get {
				if (PrimaryResult == null)
					return base.ResolvedType;
				return PrimaryResult.ResolvedType;
			}
		}
		
		public AggregatedResolveResult (params ResolveResult[] results)
		{
			resolveResults.AddRange (results);
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			foreach (ResolveResult result in resolveResults) {
				foreach (object o in result.CreateResolveResult (dom, callingMember)) {
					yield return o;
				}
			}
		}
	}
	
	public class LocalVariableResolveResult : ResolveResult
	{
		LocalVariable variable;
		public LocalVariable LocalVariable {
			get {
				return variable;
			}
		}
		
		bool   isLoopVariable;
		public bool IsLoopVariable {
			get {
				return isLoopVariable;
			}
		}
		
		public LocalVariableResolveResult (LocalVariable variable) : this (variable, false)
		{
		}
		public LocalVariableResolveResult (LocalVariable variable, bool isLoopVariable)
		{
			this.variable       = variable;
			this.isLoopVariable = isLoopVariable;
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			if (IsLoopVariable) {
				if (ResolvedType.Name == "IEnumerable" && ResolvedType.GenericArguments != null && ResolvedType.GenericArguments.Count > 0) {
					MemberResolveResult.AddType (dom, result, ResolvedType.GenericArguments [0], callingMember, StaticResolve);
				} else if (ResolvedType.Name == "IEnumerable") {
					MemberResolveResult.AddType (dom, result, DomReturnType.Object, callingMember, StaticResolve);
				} else { 
					MemberResolveResult.AddType (dom, result, dom.GetType (ResolvedType), callingMember, StaticResolve);
				}
			} else {
				MemberResolveResult.AddType (dom, result, ResolvedType, callingMember, StaticResolve);
			}
			return result;
		}
		
		public override string ToString ()
		{
			return String.Format ("[LocalVariableResolveResult: LocalVariable={0}, ResolvedType={1}]", LocalVariable, ResolvedType);
		}
	}
	
	public class ParameterResolveResult : ResolveResult
	{
		IParameter parameter;
		public IParameter Parameter {
			get {
				return parameter;
			}
		}
		
		public ParameterResolveResult (IParameter parameter)
		{
			this.parameter = parameter;
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			MemberResolveResult.AddType (dom, result, ResolvedType, callingMember, StaticResolve);
			return result;
		}
		
		public override string ToString ()
		{
			return String.Format ("[ParameterResolveResult: Parameter={0}]", Parameter);
		}
	}
	
	public class AnonymousTypeResolveResult : ResolveResult
	{
		public IType AnonymousType {
			get;
			set;
		}
		
		public AnonymousTypeResolveResult (IType anonymousType)
		{
			this.AnonymousType = anonymousType; 
			this.ResolvedType  = new DomReturnType (anonymousType);
		}
		
		public override string ToString ()
		{
			return String.Format ("[AnonymousTypeResolveResult: AnonymousType={0}]", AnonymousType);
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			foreach (IMember member in AnonymousType.Members) {
				yield return member;
			}
		}
	}
	
	public class MemberResolveResult : ResolveResult
	{
		public virtual IMember ResolvedMember {
			get;
			set;
		}
		
		protected MemberResolveResult ()
		{
		}
		
		public MemberResolveResult (IMember resolvedMember)
		{
			this.ResolvedMember = resolvedMember;
		}
		
		public MemberResolveResult (IMember resolvedMember, bool staticResolve) : base (staticResolve)
		{
			this.ResolvedMember = resolvedMember;
		}
		
		internal static void AddType (ProjectDom dom, List<object> result, IType type, IMember callingMember, bool showStatic)
		{
			AddType (dom, result, type, callingMember, showStatic, null);
		}
		
		internal static void AddType (ProjectDom dom, List<object> result, IType type, IMember callingMember, bool showStatic, Func<IMember, bool> filter)
		{
//			System.Console.WriteLine("Add Type:" + type);
			if (type == null)
				return;
			
			if (showStatic && type.ClassType == ClassType.Enum) {
				foreach (IMember member in type.Fields) {
					result.Add (member);
				}
				return;
			}
			List<IType> accessibleStaticTypes = null;
			if (callingMember != null && callingMember.DeclaringType != null)
				accessibleStaticTypes = DomType.GetAccessibleExtensionTypes (dom, callingMember.DeclaringType.CompilationUnit);
/* TODO: Typed extension methods
			IList<IReturnType> genericParameters = null;
			if (type is InstantiatedType) 
				genericParameters = ((InstantiatedType)type).GenericParameters;*/

			bool includeProtected = callingMember != null ? DomType.IncludeProtected (dom, type, callingMember.DeclaringType) : false;
			if (accessibleStaticTypes != null) {
				foreach (IMethod extensionMethod in type.GetExtensionMethods (accessibleStaticTypes))
					result.Add (extensionMethod);
			}
			foreach (IType curType in dom.GetInheritanceTree (type)) {
				if (curType.ClassType == ClassType.Interface && type.ClassType != ClassType.Interface && !(type is InstantiatedParameterType))
					continue;
				foreach (IMember member in curType.Members) {
					if (callingMember != null && !member.IsAccessibleFrom (dom, type, callingMember, includeProtected))
						continue;
// handled by member.IsAccessibleFrom
//					if (member.IsProtected && !includeProtected)
//						continue;
					if (member is IMethod && (((IMethod)member).IsConstructor || ((IMethod)member).IsFinalizer))
						continue;
					if (!showStatic && member is IType)
						continue;
					if (filter != null && filter (member))
						continue;
					if (member is IType || !(showStatic ^ (member.IsStatic || member.IsConst))) {
						result.Add (member);
					}
				}
			//	if (showStatic)
			//		break;
			}
		}
		
		internal static void AddType (ProjectDom dom, List<object> result, IReturnType returnType, IMember callingMember, bool showStatic)
		{
			if (returnType == null || returnType.FullName == "System.Void")
				return;
			if (returnType.ArrayDimensions > 0) {
				DomReturnType elementType = new DomReturnType (returnType.FullName);
				elementType.ArrayDimensions = returnType.ArrayDimensions - 1;
				for (int i = 0; i < elementType.ArrayDimensions; i++) {
					elementType.SetDimension (i, returnType.ArrayDimensions - 1);
				}
				elementType.PointerNestingLevel = returnType.PointerNestingLevel;
				
				AddType (dom, result, dom.GetArrayType (elementType), callingMember, showStatic);
				return;
			}
			IType type = dom.GetType (returnType);
			
			AddType (dom, result, type, callingMember, showStatic);
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			AddType (dom, result, ResolvedType, callingMember, StaticResolve);
			return result;
		}
		
		public override string ToString ()
		{
			return String.Format ("[MemberResolveResult: CallingType={0}, CallingMember={1}, ResolvedMember={2}, ResolvedType={3}]",
			                      CallingType,
			                      CallingMember,
			                      ResolvedMember,
			                      ResolvedType);
		}
	}
	
	public class UnresolvedMemberResolveResult : ResolveResult
	{
		public ResolveResult TargetResolveResult {
			get;
			private set;
		}
		
		public string MemberName {
			get;
			private set;
		}
		
		public UnresolvedMemberResolveResult (ResolveResult targetResolveResult, string memberName)
		{
			this.TargetResolveResult = targetResolveResult;
			this.MemberName = memberName;
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			return null;
		}
	}
	
	public class MethodResolveResult : MemberResolveResult
	{
		List<IMethod> methods = new List<IMethod> ();
		List<IMethod> originalMethods = new List<IMethod> ();
		List<IReturnType> arguments = new List<IReturnType> ();
		List<IReturnType> genericArguments = new List<IReturnType> ();
		
		/// <value>
		/// The type the methods are called on. (Required to resolve extension methods).
		/// </value>
		public IType Type {
			get;
			set;
		}
		
		public ReadOnlyCollection<IMethod> Methods {
			get {
				return methods.AsReadOnly ();
			}
		}
		
		public override IMember ResolvedMember {
			get {
				return MostLikelyMethod;
			}
		}
		
		public bool ExactMethodMatch {
			get {
				if (methods.Count == 0)
					return false;
				foreach (IMethod method in methods) {
					if (method.TypeParameters.Count != genericArguments.Count || method.Parameters.Count != arguments.Count)
						continue;
					bool match = true;
					for (int i = 0; i < method.Parameters.Count; i++) {
						if (method.Parameters[i].ReturnType.ToInvariantString () != arguments[i].ToInvariantString ()) {
							match = false;
							break;
						}
					}
					if (match)
						return true;
				}
				return false;
			}
		}
		public IMethod MostLikelyMethod {
			get {
				if (methods.Count == 0)
					return null;
				IMethod result = methods [0];
				foreach (IMethod method in methods) {
					if (method.Parameters.Any (p => p.IsParams)) {
						if (method.Parameters.Count - 1 > arguments.Count)
							continue;
					} else {
						if (method.Parameters.Count != arguments.Count)
							continue;
					}
					
					bool match = true;
					for (int i = 0; i < arguments.Count; i++) {
						if (method.Parameters.Count == 0) { // should never happen
							match = false;
							break;
						}
						
						IParameter parameter = method.Parameters[System.Math.Min (i, method.Parameters.Count - 1)];
						if (IsCompatible (parameter.ReturnType, arguments[i]))
							match = false;
					}
					if (match)
						return method;
					result = method;
				}
				return result;
			}
		}
		
		public bool IsCompatible (IReturnType baseType, IReturnType type)
		{
			if (baseType.ToInvariantString () == type.ToInvariantString ())
				return true;
			ProjectDom dom = null;
			if (CallingType == null) 
				return false;
			dom = CallingType.SourceProjectDom;
			IType b = dom.SearchType (CallingType, baseType);
			IType t = dom.SearchType (CallingType, type);
			if (b == null || t == null)
				return false;
			return dom.GetInheritanceTree (t).Any (tBase => tBase.DecoratedFullName == b.DecoratedFullName);
		}
		
		public override IReturnType ResolvedType {
			get {
				IMethod method = MostLikelyMethod;
				if (method != null) {
					IMethod instMethod = DomMethod.CreateInstantiatedGenericMethod (method, genericArguments, arguments);
					return instMethod.ReturnType;
				}
				return base.ResolvedType;
			}
		}
		
		public override IReturnType UnresolvedType {
			get {
				return ResolvedType;
			}
		}
		
		/// <summary>
		/// Flags, if the return type should be completed or this result is in 'delegate' state.
		/// </summary>
		public bool GetsInvoked {
			get;
			set;
		}

		public ReadOnlyCollection<IReturnType> GenericArguments {
			get {
				return genericArguments.AsReadOnly ();
			}
		}
		public void AddGenericArgument (IReturnType arg)
		{
			genericArguments.Add (arg);
		}
		
		public ReadOnlyCollection<IReturnType> Arguments {
			get {
				return arguments.AsReadOnly ();
			}
		}
		public void AddArgument (IReturnType arg)
		{
			arguments.Add (arg);
		}
		
		public MethodResolveResult (IMethod method)
		{
			AddMethods (new IMethod [] { method });
		}
		
		public MethodResolveResult (IEnumerable members)
		{
			AddMethods (members);
		}

		public void AddMethods (IEnumerable members) 
		{
			if (members == null)
				return;
			foreach (object member in members) {
				IMethod method = member as IMethod;
				if (method == null)
					continue;
				methods.Add (method);
				originalMethods.Add (method);
			}
		}
		
		public void ResolveExtensionMethods ()
		{
//			Console.WriteLine (" --- Resolve extension");
//			Console.WriteLine ("---Args:");
//			foreach (var arg in arguments)
//				Console.WriteLine (arg);
//			Console.WriteLine ("---GenArgs:");
//			if (genericArguments != null) {
//				foreach (var arg in genericArguments)
//					Console.WriteLine (arg);
//			} else {
//				Console.WriteLine ("<null>");
//			}
			
			Debug.Assert (originalMethods.Count == methods.Count);
			for (int i = 0; i < originalMethods.Count; i++) {
				if (originalMethods[i] is ExtensionMethod) { // Extension methods are already resolved & instantiated.
					methods[i] = originalMethods[i];
				} else if (originalMethods[i].IsExtension && Type != null) {
					methods[i] = new ExtensionMethod (Type, originalMethods[i], genericArguments, arguments);
				} else {
					methods[i] = DomMethod.CreateInstantiatedGenericMethod (originalMethods[i], genericArguments, arguments);
				}
			}
//			Console.WriteLine ("-- end resolve extension.");
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			MemberResolveResult.AddType (dom, result, GetsInvoked ? ResolvedType : DomReturnType.Delegate , callingMember, StaticResolve);
			return result;
		}
		
		public override string ToString ()
		{
			return String.Format ("[MethodResolveResult: #methods={0}]", methods.Count);
		}
	}
	
	public class CombinedMethodResolveResult : MemberResolveResult
	{
		public MemberResolveResult BaseResolveResult {
			get;
			set;
		}
		
		public MethodResolveResult MethodResolveResult {
			get;
			set;
		}
		
		public CombinedMethodResolveResult (MemberResolveResult baseResolveResult, MethodResolveResult methodResolveResult)
		{
			BaseResolveResult = baseResolveResult;
			MethodResolveResult = methodResolveResult;
			CallingType = baseResolveResult.CallingType;
			CallingMember = baseResolveResult.CallingMember;
			StaticResolve = baseResolveResult.StaticResolve;
			ResolvedMember = baseResolveResult.ResolvedMember;
			ResolvedExpression = baseResolveResult.ResolvedExpression;
		}
		
		public override IReturnType ResolvedType {
			get {
				return BaseResolveResult.ResolvedType;
			}
		}
		public override IReturnType UnresolvedType {
			get {
				return BaseResolveResult.UnresolvedType;
			}
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			return BaseResolveResult.CreateResolveResult (dom, callingMember);
		}
	}
	
	public class ThisResolveResult : ResolveResult
	{
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			if (CallingMember != null && !CallingMember.IsStatic)
				MemberResolveResult.AddType (dom, result, new DomReturnType (CallingType), callingMember, StaticResolve);
			return result;
		}
		
		public override string ToString ()
		{
			return String.Format ("[ThisResolveResult]");
		}
	}
	
	public class BaseResolveResult : ResolveResult
	{
		internal class BaseMemberDecorator : DomMemberDecorator
		{
			IType fakeDeclaringType;
			public override IType DeclaringType {
				get {
					return fakeDeclaringType;
				}
			}
			public BaseMemberDecorator (IMember member, IType fakeDeclaringType) : base (member)
			{
				this.fakeDeclaringType = fakeDeclaringType;
			}
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			if (CallingMember != null && !CallingMember.IsStatic) {
				IType baseType = dom.SearchType (CallingMember ?? CallingType, CallingType.BaseType ?? DomReturnType.Object);
				MemberResolveResult.AddType (dom, result, baseType, new BaseMemberDecorator (CallingMember, baseType), StaticResolve, m => m.IsAbstract);
			}
			return result;
		}
		public override string ToString ()
		{
			return String.Format ("[BaseResolveResult]");
		}
	}
	
	public class NamespaceResolveResult : ResolveResult
	{
		string ns;
		
		public string Namespace {
			get {
				return ns;
			}
		}
		
		public NamespaceResolveResult (string ns)
		{
			this.ns = ns;
		}
		
		public override string ToString ()
		{
			return String.Format ("[NamespaceResolveResult: Namespace={0}]", Namespace);
		}
		
		public override IEnumerable<object> CreateResolveResult (ProjectDom dom, IMember callingMember)
		{
			List<object> result = new List<object> ();
			foreach (object o in dom.GetNamespaceContents (ns, true, true)) {
				result.Add (o);
			}
			return result;
		}
	}
}
