﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="David Srbecký" email="dsrbecky@gmail.com"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections.Generic;
using Debugger.Wrappers.CorDebug;
using Debugger.Wrappers.MetaData;

namespace Debugger
{
	public partial class DebugType
	{
		class Query {
			public Type MemberType;
			public BindingFlags BindingFlags;
			public string Name;
			public uint? Token;
			
			public Query(Type memberType, BindingFlags bindingFlags, string name, Nullable<uint> token)
			{
				this.MemberType = memberType;
				this.BindingFlags = bindingFlags;
				this.Name = name;
				this.Token = token;
			}
			
			public override int GetHashCode()
			{
				int hashCode = 0;
				unchecked {
					if (MemberType != null) hashCode += 1000000007 * MemberType.GetHashCode(); 
					hashCode += 1000000009 * BindingFlags.GetHashCode();
					if (Name != null) hashCode += 1000000021 * Name.GetHashCode(); 
					hashCode += 1000000033 * Token.GetHashCode();
				}
				return hashCode;
			}
			
			public override bool Equals(object obj)
			{
				Query other = obj as Query;
				if (other == null) return false; 
				return object.Equals(this.MemberType, other.MemberType) && this.BindingFlags == other.BindingFlags && this.Name == other.Name && this.Token == other.Token;
			}
		}
		
		Dictionary<Query, object> queries = new Dictionary<Query, object>();
		
		List<T> QueryMembers<T>(BindingFlags bindingFlags) where T:MemberInfo
		{
			return QueryMembers<T>(bindingFlags, null, null);
		}
		
		T QueryMember<T>(string name) where T:MemberInfo
		{
			List<T> result = QueryMembers<T>(BindingFlags.All, name, null);
			if (result.Count > 0) {
				return result[0];
			} else {
				return null;
			}
		}
		
		T QueryMember<T>(uint token) where T:MemberInfo
		{
			List<T> result = QueryMembers<T>(BindingFlags.All, null, token);
			if (result.Count > 0) {
				return result[0];
			} else {
				return null;
			}
		}
		
		List<T> QueryMembers<T>(BindingFlags bindingFlags, string name, Nullable<uint> token) where T:MemberInfo
		{
			Query query = new Query(typeof(T), bindingFlags, name, token);
			
			if (queries.ContainsKey(query)) {
				return (List<T>)queries[query];
			}
			
			List<T> results = new List<T>();
			foreach(MemberInfo memberInfo in members) {
				// Filter by type
				if (!(memberInfo is T)) continue; // Reject item
				// Filter by access
				if ((bindingFlags & (BindingFlags.Public | BindingFlags.NonPublic)) != 0) {
					if (memberInfo.IsPublic) {
						// If we do not want public members
						if ((bindingFlags & BindingFlags.Public) == 0) continue; // Reject item
					} else {
						if ((bindingFlags & BindingFlags.NonPublic) == 0) continue; // Reject item
					}
				}
				// Filter by static / instance
				if ((bindingFlags & (BindingFlags.Static | BindingFlags.Instance)) != 0) {
					if (memberInfo.IsStatic) {
						if ((bindingFlags & BindingFlags.Static) == 0) continue; // Reject item
					} else {
						if ((bindingFlags & BindingFlags.Instance) == 0) continue; // Reject item
					}
				}
				// Filter by name
				if (name != null) {
					if (memberInfo.Name != name) continue; // Reject item
				}
				// Filter by token
				if (token.HasValue) {
					if (memberInfo.MetadataToken != token.Value) continue; // Reject item
				}
				results.Add((T)memberInfo);
			}
			
			queries[query] = results;
			return results;
		}
		
		#region Queries
		
		/// <summary> Return all public members.</summary>
		public IList<MemberInfo> GetMembers()
		{
			return QueryMembers<MemberInfo>(BindingFlags.Public);
		}
		
		/// <summary> Return all members satisfing binding flags.</summary>
		public IList<MemberInfo> GetMembers(BindingFlags bindingFlags)
		{
			return QueryMembers<MemberInfo>(bindingFlags);
		}
		
		/// <summary> Return first member with the given name</summary>
		public MemberInfo GetMember(string name)
		{
			return QueryMember<MemberInfo>(name);
		}
		
		/// <summary> Return first member with the given token</summary>
		public MemberInfo GetMember(uint token)
		{
			return QueryMember<MemberInfo>(token);
		}
		
		
		/// <summary> Return all public fields.</summary>
		public IList<FieldInfo> GetFields()
		{
			return QueryMembers<FieldInfo>(BindingFlags.Public);
		}
		
		/// <summary> Return all fields satisfing binding flags.</summary>
		public IList<FieldInfo> GetFields(BindingFlags bindingFlags)
		{
			return QueryMembers<FieldInfo>(bindingFlags);
		}
		
		/// <summary> Return first field with the given name</summary>
		public FieldInfo GetField(string name)
		{
			return QueryMember<FieldInfo>(name);
		}
		
		/// <summary> Return first field with the given token</summary>
		public FieldInfo GetField(uint token)
		{
			return QueryMember<FieldInfo>(token);
		}
		
		
		/// <summary> Return all public methods.</summary>
		public IList<MethodInfo> GetMethods()
		{
			return QueryMembers<MethodInfo>(BindingFlags.Public);
		}
		
		/// <summary> Return all methods satisfing binding flags.</summary>
		public IList<MethodInfo> GetMethods(BindingFlags bindingFlags)
		{
			return QueryMembers<MethodInfo>(bindingFlags);
		}
		
		/// <summary> Return first method with the given name</summary>
		public MethodInfo GetMethod(string name)
		{
			return QueryMember<MethodInfo>(name);
		}
		
		/// <summary> Return first method with the given token</summary>
		public MethodInfo GetMethod(uint token)
		{
			return QueryMember<MethodInfo>(token);
		}
		
		
		/// <summary> Return all public properties.</summary>
		public IList<PropertyInfo> GetProperties()
		{
			return QueryMembers<PropertyInfo>(BindingFlags.Public);
		}
		
		/// <summary> Return all properties satisfing binding flags.</summary>
		public IList<PropertyInfo> GetProperties(BindingFlags bindingFlags)
		{
			return QueryMembers<PropertyInfo>(bindingFlags);
		}
		
		/// <summary> Return first property with the given name</summary>
		public PropertyInfo GetProperty(string name)
		{
			return QueryMember<PropertyInfo>(name);
		}
		
		/// <summary> Return first property with the given token</summary>
		public PropertyInfo GetProperty(uint token)
		{
			return QueryMember<PropertyInfo>(token);
		}
		
		#endregion
		
		/// <summary>
		/// Returns simple managed type coresponding to the debug type.
		/// Any class yields System.Object
		/// </summary>
		public System.Type ManagedType {
			get {
				switch(this.corElementType) {
					case CorElementType.BOOLEAN: return typeof(System.Boolean);
					case CorElementType.CHAR: return typeof(System.Char);
					case CorElementType.I1: return typeof(System.SByte);
					case CorElementType.U1: return typeof(System.Byte);
					case CorElementType.I2: return typeof(System.Int16);
					case CorElementType.U2: return typeof(System.UInt16);
					case CorElementType.I4: return typeof(System.Int32);
					case CorElementType.U4: return typeof(System.UInt32);
					case CorElementType.I8: return typeof(System.Int64);
					case CorElementType.U8: return typeof(System.UInt64);
					case CorElementType.R4: return typeof(System.Single);
					case CorElementType.R8: return typeof(System.Double);
					case CorElementType.I: return typeof(int);
					case CorElementType.U: return typeof(uint);
					case CorElementType.SZARRAY:
					case CorElementType.ARRAY: return typeof(System.Array);
					case CorElementType.OBJECT: return typeof(System.Object);
					case CorElementType.STRING: return typeof(System.String);
					default: return null;
				}
			}
		}
		
		/*
		 * Find the super class manually - unused since we have ICorDebugType.GetBase() in .NET 2.0
		 * 
		protected static ICorDebugClass GetSuperClass(Process process, ICorDebugClass currClass)
		{
			Module currModule = process.GetModule(currClass.Module);
			uint superToken = currModule.MetaData.GetTypeDefProps(currClass.Token).SuperClassToken;
			
			// It has no base class
			if ((superToken & 0x00FFFFFF) == 0x00000000) return null;
			
			// TypeDef - Localy defined
			if ((superToken & 0xFF000000) == 0x02000000) {
				return currModule.CorModule.GetClassFromToken(superToken);
			}
			
			// TypeSpec - generic class whith 'which'
			if ((superToken & 0xFF000000) == 0x1B000000) {
				// Walkaround - fake 'object' type
				string fullTypeName = "System.Object";
				
				foreach (Module superModule in process.Modules) {
					try	{
						uint token = superModule.MetaData.FindTypeDefByName(fullTypeName, 0).Token;
						return superModule.CorModule.GetClassFromToken(token);
					} catch {
						continue;
					}
				}
			}
			
			// TypeRef - Referencing to external assembly
			if ((superToken & 0xFF000000) == 0x01000000) {
				string fullTypeName = currModule.MetaData.GetTypeRefProps(superToken).Name;
				
				foreach (Module superModule in process.Modules) {
					// TODO: Does not work for nested
					// TODO: preservesig
					try	{
						uint token = superModule.MetaData.FindTypeDefByName(fullTypeName, 0).Token;
						return superModule.CorModule.GetClassFromToken(token);
					} catch {
						continue;
					}
				}
			}
			
			// TODO: Can also be TypeSpec = 0x1b000000
			
			throw new DebuggerException("Superclass not found");
		}
		 */
	}
}
