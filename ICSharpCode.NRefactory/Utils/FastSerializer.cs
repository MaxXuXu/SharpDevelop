﻿// Copyright (c) 2011 Daniel Grunwald
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace ICSharpCode.NRefactory.Utils
{
	public class FastSerializer
	{
		#region Serialization
		sealed class ReferenceComparer : IEqualityComparer<object>
		{
			bool IEqualityComparer<object>.Equals(object a, object b)
			{
				return a == b;
			}
			
			int IEqualityComparer<object>.GetHashCode(object obj)
			{
				return RuntimeHelpers.GetHashCode(obj);
			}
		}
		
		sealed class SerializationContext
		{
			readonly Dictionary<object, int> objectToID = new Dictionary<object, int>(new ReferenceComparer());
			readonly List<object> instances = new List<object>(); // index: object ID
			readonly List<int> typeIDs = new List<int>(); // index: object ID
			int stringTypeID = -1;
			int typeCountForObjects = 0;
			
			readonly Dictionary<Type, int> typeToID = new Dictionary<Type, int>();
			readonly List<Type> types = new List<Type>(); // index: type ID
			readonly List<ObjectWriter> writers = new List<ObjectWriter>(); // index: type ID
			
			readonly FastSerializer fastSerializer;
			public readonly BinaryWriter writer;
			
			internal SerializationContext(FastSerializer fastSerializer, BinaryWriter writer)
			{
				this.fastSerializer = fastSerializer;
				this.writer = writer;
				instances.Add(null); // use object ID 0 for null
				typeIDs.Add(-1);
			}
			
			#region Scanning
			/// <summary>
			/// Marks an instance for future scanning.
			/// </summary>
			public void Mark(object instance)
			{
				if (instance == null || objectToID.ContainsKey(instance))
					return;
				Log(" Mark {0}", instance.GetType().Name);
				
				objectToID.Add(instance, instances.Count);
				instances.Add(instance);
			}
			
			internal void Scan()
			{
				Log("Scanning...");
				List<ObjectScanner> objectScanners = new List<ObjectScanner>(); // index: type ID
				// starting from 1, because index 0 is null
				for (int i = 1; i < instances.Count; i++) {
					object instance = instances[i];
					ISerializable serializable = instance as ISerializable;
					Type type = instance.GetType();
					Log("Scan #{0}: {1}", i, type.Name);
					int typeID;
					if (!typeToID.TryGetValue(type, out typeID)) {
						typeID = types.Count;
						typeToID.Add(type, typeID);
						types.Add(type);
						Log("Registered type %{0}: {1}", typeID, type);
						if (type == typeof(string)) {
							stringTypeID = typeID;
						}
						objectScanners.Add(serializable != null ? null : fastSerializer.GetScanner(type));
						writers.Add(serializable != null ? serializationInfoWriter : fastSerializer.GetWriter(type));
					}
					typeIDs.Add(typeID);
					if (serializable != null) {
						SerializationInfo info = new SerializationInfo(type, fastSerializer.formatterConverter);
						serializable.GetObjectData(info, fastSerializer.streamingContext);
						instances[i] = info;
						foreach (SerializationEntry entry in info) {
							Mark(entry.Value);
						}
					} else {
						objectScanners[typeID](this, instance);
					}
				}
			}
			#endregion
			
			#region Scan Types
			internal void ScanTypes()
			{
				typeCountForObjects = types.Count;
				for (int i = 0; i < types.Count; i++) {
					foreach (FieldInfo field in GetSerializableFields(types[i])) {
						if (!typeToID.ContainsKey(field.FieldType)) {
							typeToID.Add(field.FieldType, types.Count);
							types.Add(field.FieldType);
						}
					}
				}
			}
			#endregion
			
			#region Writing
			public void WriteObjectID(object instance)
			{
				int id = (instance == null) ? 0 : objectToID[instance];
				if (instances.Count <= ushort.MaxValue)
					writer.Write((ushort)id);
				else
					writer.Write(id);
			}
			
			internal void Write()
			{
				Log("Writing...");
				// Write out type information
				writer.Write(types.Count);
				writer.Write(instances.Count);
				writer.Write(typeCountForObjects);
				writer.Write(stringTypeID);
				foreach (Type type in types) {
					writer.Write(type.AssemblyQualifiedName);
				}
				foreach (Type type in types) {
					if (type.IsArray || type.IsPrimitive || typeof(ISerializable).IsAssignableFrom(type)) {
						writer.Write(byte.MaxValue);
					} else {
						var fields = GetSerializableFields(type);
						if (fields.Count >= byte.MaxValue)
							throw new SerializationException("Too many fields.");
						writer.Write((byte)fields.Count);
						foreach (var field in fields) {
							int typeID = typeToID[field.FieldType];
							if (types.Count <= ushort.MaxValue)
								writer.Write((ushort)typeID);
							else
								writer.Write(typeID);
							writer.Write(field.Name);
						}
					}
				}
				
				// Write out information necessary to create the instances
				// starting from 1, because index 0 is null
				for (int i = 1; i < instances.Count; i++) {
					int typeID = typeIDs[i];
					if (types.Count <= ushort.MaxValue)
						writer.Write((ushort)typeID);
					else
						writer.Write(typeID);
					if (typeID == stringTypeID) {
						// Strings are written to the output immediately
						// - we can't create an empty string and fill it later
						writer.Write((string)instances[i]);
					} else if (types[typeID].IsArray) {
						// For arrays, write down the length, because we need that to create the array instance
						writer.Write(((Array)instances[i]).Length);
					}
				}
				// Write out information necessary to fill data into the instances
				for (int i = 1; i < instances.Count; i++) {
					Log("0x{2:x6}, Write #{0}: {1}", i, types[typeIDs[i]].Name, writer.BaseStream.Position);
					writers[typeIDs[i]](this, instances[i]);
				}
				Log("Serialization done.");
			}
			#endregion
		}
		
		#region Object Scanners
		delegate void ObjectScanner(SerializationContext context, object instance);
		
		static readonly MethodInfo mark = typeof(SerializationContext).GetMethod("Mark", new[] { typeof(object) });
		static readonly FieldInfo writerField = typeof(SerializationContext).GetField("writer");
		
		Dictionary<Type, ObjectScanner> scanners = new Dictionary<Type, ObjectScanner>();
		
		ObjectScanner GetScanner(Type type)
		{
			ObjectScanner scanner;
			if (!scanners.TryGetValue(type, out scanner)) {
				scanner = CreateScanner(type);
				scanners.Add(type, scanner);
			}
			return scanner;
		}
		
		ObjectScanner CreateScanner(Type type)
		{
			bool isArray = type.IsArray;
			if (isArray) {
				if (type.GetArrayRank() != 1)
					throw new NotImplementedException();
				type = type.GetElementType();
				if (!type.IsValueType) {
					return delegate (SerializationContext context, object array) {
						foreach (object val in (object[])array) {
							context.Mark(val);
						}
					};
				}
			}
			for (Type baseType = type; baseType != null; baseType = baseType.BaseType) {
				if (!baseType.IsSerializable)
					throw new SerializationException("Type " + baseType + " is not [Serializable].");
			}
			List<FieldInfo> fields = GetSerializableFields(type);
			fields.RemoveAll(f => !IsReferenceOrContainsReferences(f.FieldType));
			if (fields.Count == 0) {
				// The scanner has nothing to do for this object.
				return delegate { };
			}
			
			DynamicMethod dynamicMethod = new DynamicMethod(
				(isArray ? "ScanArray_" : "Scan_") + type.Name,
				typeof(void), new [] { typeof(SerializationContext), typeof(object) },
				true);
			ILGenerator il = dynamicMethod.GetILGenerator();
			
			
			if (isArray) {
				var instance = il.DeclareLocal(type.MakeArrayType());
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type.MakeArrayType());
				il.Emit(OpCodes.Stloc, instance); // instance = (type[])arg_1;
				
				// for (int i = 0; i < instance.Length; i++) scan instance[i];
				var loopStart = il.DefineLabel();
				var loopHead = il.DefineLabel();
				var loopVariable = il.DeclareLocal(typeof(int));
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable = 0
				il.Emit(OpCodes.Br, loopHead); // goto loopHead;
				
				il.MarkLabel(loopStart);
				
				il.Emit(OpCodes.Ldloc, instance); // instance
				il.Emit(OpCodes.Ldloc, loopVariable); // instance, loopVariable
				il.Emit(OpCodes.Ldelem, type); // &instance[loopVariable]
				EmitScanValueType(il, type);
				
				
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldc_I4_1); // loopVariable, 1
				il.Emit(OpCodes.Add); // loopVariable+1
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable++;
				
				il.MarkLabel(loopHead);
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldloc, instance); // loopVariable, instance
				il.Emit(OpCodes.Ldlen); // loopVariable, instance.Length
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Blt, loopStart); // if (loopVariable < instance.Length) goto loopStart;
			} else if (type.IsValueType) {
				// boxed value type
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Unbox_Any, type);
				EmitScanValueType(il, type);
			} else {
				// reference type
				var instance = il.DeclareLocal(type);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type);
				il.Emit(OpCodes.Stloc, instance); // instance = (type)arg_1;
				
				foreach (FieldInfo field in fields) {
					EmitScanField(il, instance, field); // scan instance.Field
				}
			}
			il.Emit(OpCodes.Ret);
			return (ObjectScanner)dynamicMethod.CreateDelegate(typeof(ObjectScanner));
		}

		/// <summary>
		/// Emit 'scan instance.Field'.
		/// Stack transition: ... => ...
		/// </summary>
		void EmitScanField(ILGenerator il, LocalBuilder instance, FieldInfo field)
		{
			if (field.FieldType.IsValueType) {
				il.Emit(OpCodes.Ldloc, instance); // instance
				il.Emit(OpCodes.Ldfld, field); // instance.field
				EmitScanValueType(il, field.FieldType);
			} else {
				il.Emit(OpCodes.Ldarg_0); // context
				il.Emit(OpCodes.Ldloc, instance); // context, instance
				il.Emit(OpCodes.Ldfld, field); // context, instance.field
				il.Emit(OpCodes.Call, mark); // context.Mark(instance.field);
			}
		}

		/// <summary>
		/// Stack transition: ..., value => ...
		/// </summary>
		void EmitScanValueType(ILGenerator il, Type valType)
		{
			var fieldRef = il.DeclareLocal(valType);
			il.Emit(OpCodes.Stloc, fieldRef);
			
			foreach (FieldInfo field in GetSerializableFields(valType)) {
				if (IsReferenceOrContainsReferences(field.FieldType)) {
					EmitScanField(il, fieldRef, field);
				}
			}
		}

		static List<FieldInfo> GetSerializableFields(Type type)
		{
			List<FieldInfo> fields = new List<FieldInfo>();
			for (Type baseType = type; baseType != null; baseType = baseType.BaseType) {
				FieldInfo[] declFields = baseType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
				Array.Sort(declFields, (a,b) => a.Name.CompareTo(b.Name));
				fields.AddRange(declFields);
			}
			fields.RemoveAll(f => f.IsNotSerialized);
			return fields;
		}

		static bool IsReferenceOrContainsReferences(Type type)
		{
			if (!type.IsValueType)
				return true;
			if (type.IsPrimitive)
				return false;
			foreach (FieldInfo field in GetSerializableFields(type)) {
				if (IsReferenceOrContainsReferences(field.FieldType))
					return true;
			}
			return false;
		}
		#endregion

		#region Object Writers
		delegate void ObjectWriter(SerializationContext context, object instance);

		static readonly MethodInfo writeObjectID = typeof(SerializationContext).GetMethod("WriteObjectID", new[] { typeof(object) });

		static readonly MethodInfo writeByte = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(byte) });
		static readonly MethodInfo writeShort = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(short) });
		static readonly MethodInfo writeInt = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(int) });
		static readonly MethodInfo writeLong = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(long) });
		static readonly MethodInfo writeFloat = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(float) });
		static readonly MethodInfo writeDouble = typeof(BinaryWriter).GetMethod("Write", new[] { typeof(double) });
		OpCode callVirt = OpCodes.Callvirt;

		static readonly ObjectWriter serializationInfoWriter = delegate(SerializationContext context, object instance) {
			BinaryWriter writer = context.writer;
			SerializationInfo info = (SerializationInfo)instance;
			writer.Write(info.MemberCount);
			foreach (SerializationEntry entry in info) {
				writer.Write(entry.Name);
				context.WriteObjectID(entry.Value);
			}
		};

		Dictionary<Type, ObjectWriter> writers = new Dictionary<Type, ObjectWriter>();

		ObjectWriter GetWriter(Type type)
		{
			ObjectWriter writer;
			if (!writers.TryGetValue(type, out writer)) {
				writer = CreateWriter(type);
				writers.Add(type, writer);
			}
			return writer;
		}

		ObjectWriter CreateWriter(Type type)
		{
			if (type == typeof(string)) {
				// String contents are written in the object creation section,
				// not into the field value section.
				return delegate {};
			}
			bool isArray = type.IsArray;
			if (isArray) {
				if (type.GetArrayRank() != 1)
					throw new NotImplementedException();
				type = type.GetElementType();
				if (!type.IsValueType) {
					return delegate (SerializationContext context, object array) {
						foreach (object val in (object[])array) {
							context.WriteObjectID(val);
						}
					};
				} else if (type == typeof(byte[])) {
					return delegate (SerializationContext context, object array) {
						context.writer.Write((byte[])array);
					};
				}
			}
			List<FieldInfo> fields = GetSerializableFields(type);
			if (fields.Count == 0) {
				// The writer has nothing to do for this object.
				return delegate { };
			}
			
			
			DynamicMethod dynamicMethod = new DynamicMethod(
				(isArray ? "WriteArray_" : "Write_") + type.Name,
				typeof(void), new [] { typeof(SerializationContext), typeof(object) },
				true);
			ILGenerator il = dynamicMethod.GetILGenerator();
			
			var writer = il.DeclareLocal(typeof(BinaryWriter));
			
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, writerField);
			il.Emit(OpCodes.Stloc, writer); // writer = context.writer;
			
			if (isArray) {
				var instance = il.DeclareLocal(type.MakeArrayType());
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type.MakeArrayType());
				il.Emit(OpCodes.Stloc, instance); // instance = (type[])arg_1;
				
				// for (int i = 0; i < instance.Length; i++) write instance[i];
				
				var loopStart = il.DefineLabel();
				var loopHead = il.DefineLabel();
				var loopVariable = il.DeclareLocal(typeof(int));
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable = 0
				il.Emit(OpCodes.Br, loopHead); // goto loopHead;
				
				il.MarkLabel(loopStart);
				
				if (type.IsEnum || type.IsPrimitive) {
					if (type.IsEnum) {
						type = type.GetEnumUnderlyingType();
					}
					Debug.Assert(type.IsPrimitive);
					il.Emit(OpCodes.Ldloc, writer); // writer
					il.Emit(OpCodes.Ldloc, instance); // writer, instance
					il.Emit(OpCodes.Ldloc, loopVariable); // writer, instance, loopVariable
					switch (Type.GetTypeCode(type)) {
						case TypeCode.Boolean:
						case TypeCode.SByte:
						case TypeCode.Byte:
							il.Emit(OpCodes.Ldelem_I1); // writer, instance[loopVariable]
							il.Emit(callVirt, writeByte); // writer.Write(instance[loopVariable]);
							break;
						case TypeCode.Char:
						case TypeCode.Int16:
						case TypeCode.UInt16:
							il.Emit(OpCodes.Ldelem_I2); // writer, instance[loopVariable]
							il.Emit(callVirt, writeShort); // writer.Write(instance[loopVariable]);
							break;
						case TypeCode.Int32:
						case TypeCode.UInt32:
							il.Emit(OpCodes.Ldelem_I4);  // writer, instance[loopVariable]
							il.Emit(callVirt, writeInt); // writer.Write(instance[loopVariable]);
							break;
						case TypeCode.Int64:
						case TypeCode.UInt64:
							il.Emit(OpCodes.Ldelem_I8);  // writer, instance[loopVariable]
							il.Emit(callVirt, writeLong); // writer.Write(instance[loopVariable]);
							break;
						case TypeCode.Single:
							il.Emit(OpCodes.Ldelem_R4);  // writer, instance[loopVariable]
							il.Emit(callVirt, writeFloat); // writer.Write(instance[loopVariable]);
							break;
						case TypeCode.Double:
							il.Emit(OpCodes.Ldelem_R8);  // writer, instance[loopVariable]
							il.Emit(callVirt, writeDouble); // writer.Write(instance[loopVariable]);
							break;
						default:
							throw new NotSupportedException("Unknown primitive type " + type);
					}
				} else {
					il.Emit(OpCodes.Ldloc, instance); // instance
					il.Emit(OpCodes.Ldloc, loopVariable); // instance, loopVariable
					il.Emit(OpCodes.Ldelem, type); // instance[loopVariable]
					EmitWriteValueType(il, writer, type);
				}
				
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldc_I4_1); // loopVariable, 1
				il.Emit(OpCodes.Add); // loopVariable+1
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable++;
				
				il.MarkLabel(loopHead);
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldloc, instance); // loopVariable, instance
				il.Emit(OpCodes.Ldlen); // loopVariable, instance.Length
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Blt, loopStart); // if (loopVariable < instance.Length) goto loopStart;
			} else if (type.IsValueType) {
				// boxed value type
				if (type.IsEnum || type.IsPrimitive) {
					il.Emit(OpCodes.Ldloc, writer);
					il.Emit(OpCodes.Ldarg_1);
					il.Emit(OpCodes.Unbox_Any, type);
					WritePrimitiveValue(il, type);
				} else {
					il.Emit(OpCodes.Ldarg_1);
					il.Emit(OpCodes.Unbox_Any, type);
					EmitWriteValueType(il, writer, type);
				}
			} else {
				// reference type
				var instance = il.DeclareLocal(type);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type);
				il.Emit(OpCodes.Stloc, instance); // instance = (type)arg_1;
				
				foreach (FieldInfo field in fields) {
					EmitWriteField(il, writer, instance, field); // write instance.Field
				}
			}
			il.Emit(OpCodes.Ret);
			return (ObjectWriter)dynamicMethod.CreateDelegate(typeof(ObjectWriter));
		}

		/// <summary>
		/// Emit 'write instance.Field'.
		/// Stack transition: ... => ...
		/// </summary>
		void EmitWriteField(ILGenerator il, LocalBuilder writer, LocalBuilder instance, FieldInfo field)
		{
			Type fieldType = field.FieldType;
			if (fieldType.IsValueType) {
				if (fieldType.IsPrimitive || fieldType.IsEnum) {
					il.Emit(OpCodes.Ldloc, writer); // writer
					il.Emit(OpCodes.Ldloc, instance); // writer, instance
					il.Emit(OpCodes.Ldfld, field); // writer, instance.field
					WritePrimitiveValue(il, fieldType);
				} else {
					il.Emit(OpCodes.Ldloc, instance); // instance
					il.Emit(OpCodes.Ldfld, field); // instance.field
					EmitWriteValueType(il, writer, fieldType);
				}
			} else {
				il.Emit(OpCodes.Ldarg_0); // context
				il.Emit(OpCodes.Ldloc, instance); // context, instance
				il.Emit(OpCodes.Ldfld, field); // context, instance.field
				il.Emit(OpCodes.Call, writeObjectID); // context.WriteObjectID(instance.field);
			}
		}
		
		/// <summary>
		/// Writes a primitive value of the specified type.
		/// Stack transition: ..., writer, value => ...
		/// </summary>
		void WritePrimitiveValue(ILGenerator il, Type fieldType)
		{
			if (fieldType.IsEnum) {
				fieldType = fieldType.GetEnumUnderlyingType();
				Debug.Assert(fieldType.IsPrimitive);
			}
			switch (Type.GetTypeCode(fieldType)) {
				case TypeCode.Boolean:
				case TypeCode.SByte:
				case TypeCode.Byte:
					il.Emit(callVirt, writeByte); // writer.Write(value);
					break;
				case TypeCode.Char:
				case TypeCode.Int16:
				case TypeCode.UInt16:
					il.Emit(callVirt, writeShort); // writer.Write(value);
					break;
				case TypeCode.Int32:
				case TypeCode.UInt32:
					il.Emit(callVirt, writeInt); // writer.Write(value);
					break;
				case TypeCode.Int64:
				case TypeCode.UInt64:
					il.Emit(callVirt, writeLong); // writer.Write(value);
					break;
				case TypeCode.Single:
					il.Emit(callVirt, writeFloat); // writer.Write(value);
					break;
				case TypeCode.Double:
					il.Emit(callVirt, writeDouble); // writer.Write(value);
					break;
				default:
					throw new NotSupportedException("Unknown primitive type " + fieldType);
			}
		}

		/// <summary>
		/// Stack transition: ..., value => ...
		/// </summary>
		void EmitWriteValueType(ILGenerator il, LocalBuilder writer, Type valType)
		{
			Debug.Assert(valType.IsValueType);
			Debug.Assert(!(valType.IsEnum || valType.IsPrimitive));
			
			var fieldVal = il.DeclareLocal(valType);
			il.Emit(OpCodes.Stloc, fieldVal);
			
			foreach (FieldInfo field in GetSerializableFields(valType)) {
				EmitWriteField(il, writer, fieldVal, field);
			}
		}
		#endregion

		StreamingContext streamingContext = new StreamingContext(StreamingContextStates.All);
		FormatterConverter formatterConverter = new FormatterConverter();

		public void Serialize(Stream stream, object instance)
		{
			Serialize(new BinaryWriterWith7BitEncodedInts(stream), instance);
		}

		public void Serialize(BinaryWriter writer, object instance)
		{
			SerializationContext context = new SerializationContext(this, writer);
			context.Mark(instance);
			context.Scan();
			context.ScanTypes();
			context.Write();
		}

		delegate void TypeSerializer(object instance, SerializationContext context);
		#endregion

		#region Deserialization
		sealed class DeserializationContext
		{
			public Type[] Types; // index: type ID
			public ObjectReader[] ObjectReaders; // index: type ID
			
			public object[] Objects; // index: object ID
			
			public BinaryReader Reader;
			
			public object ReadObject()
			{
				if (this.Objects.Length <= ushort.MaxValue)
					return this.Objects[Reader.ReadUInt16()];
				else
					return this.Objects[Reader.ReadInt32()];
			}
			
			#region DeserializeTypeDescriptions
			internal int ReadFieldTypeID()
			{
				if (this.Types.Length <= ushort.MaxValue)
					return Reader.ReadUInt16();
				else
					return Reader.ReadInt32();
			}
			
			internal void DeserializeTypeDescriptions(FastSerializer fastSerializer)
			{
				for (int i = 0; i < this.Types.Length; i++) {
					Type type = this.Types[i];
					bool isCustomSerialization = typeof(ISerializable).IsAssignableFrom(type);
					bool typeIsSpecial = type.IsArray || type.IsPrimitive || isCustomSerialization;
					
					byte serializedFieldCount = Reader.ReadByte();
					if (serializedFieldCount == byte.MaxValue) {
						// special type
						if (!typeIsSpecial)
							throw new SerializationException("Type " + type + " was serialized as special type, but isn't special now.");
					} else {
						if (typeIsSpecial)
							throw new SerializationException("Type " + type.FullName + " wasn't serialized as special type, but is special now.");
						
						var availableFields = GetSerializableFields(this.Types[i]);
						if (availableFields.Count != serializedFieldCount)
							throw new SerializationException("Number of fields on " + type.FullName + " has changed.");
						for (int j = 0; j < serializedFieldCount; j++) {
							int fieldTypeID = ReadFieldTypeID();
							
							string fieldName = Reader.ReadString();
							FieldInfo fieldInfo = availableFields[j];
							if (fieldInfo.Name != fieldName)
								throw new SerializationException("Field mismatch on type " + type.FullName);
							if (fieldInfo.FieldType != this.Types[fieldTypeID])
								throw new SerializationException(type.FullName + "." + fieldName + " was serialized as " + this.Types[fieldTypeID] + ", but now is " + fieldInfo.FieldType);
						}
					}
					
					if (i < this.ObjectReaders.Length && !isCustomSerialization)
						this.ObjectReaders[i] = fastSerializer.GetReader(type);
				}
			}
			#endregion
		}
		
		delegate void ObjectReader(DeserializationContext context, object instance);
		
		public object Deserialize(Stream stream)
		{
			return Deserialize(new BinaryReaderWith7BitEncodedInts(stream));
		}
		
		public object Deserialize(BinaryReader reader)
		{
			DeserializationContext context = new DeserializationContext();
			context.Reader = reader;
			context.Types = new Type[reader.ReadInt32()];
			context.Objects = new object[reader.ReadInt32()];
			context.ObjectReaders = new ObjectReader[reader.ReadInt32()];
			int stringTypeID = reader.ReadInt32();
			for (int i = 0; i < context.Types.Length; i++) {
				string typeName = reader.ReadString();
				Type type = Type.GetType(typeName);
				if (type == null)
					throw new SerializationException("Could not find " + typeName);
				context.Types[i] = type;
			}
			context.DeserializeTypeDescriptions(this);
			int[] typeIDByObjectID = new int[context.Objects.Length];
			for (int i = 1; i < context.Objects.Length; i++) {
				int typeID = context.ReadFieldTypeID();
				
				object instance;
				if (typeID == stringTypeID) {
					instance = reader.ReadString();
				} else {
					Type type = context.Types[typeID];
					if (type.IsArray) {
						int length = reader.ReadInt32();
						instance = Array.CreateInstance(type.GetElementType(), length);
					} else {
						instance = FormatterServices.GetUninitializedObject(type);
					}
				}
				context.Objects[i] = instance;
				typeIDByObjectID[i] = typeID;
			}
			List<CustomDeserialization> customDeserializatons = new List<CustomDeserialization>();
			for (int i = 1; i < context.Objects.Length; i++) {
				object instance = context.Objects[i];
				int typeID = typeIDByObjectID[i];
				Log("0x{2:x6} Read #{0}: {1}", i, context.Types[typeID].Name, reader.BaseStream.Position);
				ISerializable serializable = instance as ISerializable;
				if (serializable != null) {
					Type type = context.Types[typeID];
					SerializationInfo info = new SerializationInfo(type, formatterConverter);
					int count = reader.ReadInt32();
					for (int j = 0; j < count; j++) {
						string name = reader.ReadString();
						object val = context.ReadObject();
						info.AddValue(name, val);
					}
					CustomDeserializationAction action = GetCustomDeserializationAction(type);
					customDeserializatons.Add(new CustomDeserialization(instance, info, action));
				} else {
					context.ObjectReaders[typeID](context, instance);
				}
			}
			Log("File was read successfully, now running {0} custom deserializations...", customDeserializatons.Count);
			foreach (CustomDeserialization customDeserializaton in customDeserializatons) {
				customDeserializaton.Run(streamingContext);
			}
			for (int i = 1; i < context.Objects.Length; i++) {
				IDeserializationCallback dc = context.Objects[i] as IDeserializationCallback;
				if (dc != null)
					dc.OnDeserialization(null);
			}
			
			if (context.Objects.Length <= 1)
				return null;
			else
				return context.Objects[1];
		}
		
		#region Object Reader
		static readonly FieldInfo readerField = typeof(DeserializationContext).GetField("Reader");
		static readonly MethodInfo readObject = typeof(DeserializationContext).GetMethod("ReadObject");
		
		static readonly MethodInfo readByte = typeof(BinaryReader).GetMethod("ReadByte");
		static readonly MethodInfo readShort = typeof(BinaryReader).GetMethod("ReadInt16");
		static readonly MethodInfo readInt = typeof(BinaryReader).GetMethod("ReadInt32");
		static readonly MethodInfo readLong = typeof(BinaryReader).GetMethod("ReadInt64");
		static readonly MethodInfo readFloat = typeof(BinaryReader).GetMethod("ReadSingle");
		static readonly MethodInfo readDouble = typeof(BinaryReader).GetMethod("ReadDouble");
		
		Dictionary<Type, ObjectReader> readers = new Dictionary<Type, ObjectReader>();

		ObjectReader GetReader(Type type)
		{
			ObjectReader reader;
			if (!readers.TryGetValue(type, out reader)) {
				reader = CreateReader(type);
				readers.Add(type, reader);
			}
			return reader;
		}
		
		ObjectReader CreateReader(Type type)
		{
			if (type == typeof(string)) {
				// String contents are written in the object creation section,
				// not into the field value section; so there's nothing to read here.
				return delegate {};
			}
			bool isArray = type.IsArray;
			if (isArray) {
				if (type.GetArrayRank() != 1)
					throw new NotImplementedException();
				type = type.GetElementType();
				if (!type.IsValueType) {
					return delegate (DeserializationContext context, object arrayInstance) {
						object[] array = (object[])arrayInstance;
						for (int i = 0; i < array.Length; i++) {
							array[i] = context.ReadObject();
						}
					};
				} else if (type == typeof(byte[])) {
					return delegate (DeserializationContext context, object arrayInstance) {
						byte[] array = (byte[])arrayInstance;
						BinaryReader binaryReader = context.Reader;
						int pos = 0;
						int bytesRead;
						do {
							bytesRead = binaryReader.Read(array, pos, array.Length - pos);
							pos += bytesRead;
						} while (bytesRead > 0);
						if (pos != array.Length)
							throw new EndOfStreamException();
					};
				}
			}
			var fields = GetSerializableFields(type);
			if (fields.Count == 0) {
				// The reader has nothing to do for this object.
				return delegate { };
			}
			
			DynamicMethod dynamicMethod = new DynamicMethod(
				(isArray ? "ReadArray_" : "Read_") + type.Name,
				MethodAttributes.Public | MethodAttributes.Static,
				CallingConventions.Standard,
				typeof(void), new [] { typeof(DeserializationContext), typeof(object) },
				type,
				true);
			ILGenerator il = dynamicMethod.GetILGenerator();
			
			var reader = il.DeclareLocal(typeof(BinaryReader));
			
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, readerField);
			il.Emit(OpCodes.Stloc, reader); // reader = context.reader;
			
			if (isArray) {
				var instance = il.DeclareLocal(type.MakeArrayType());
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type.MakeArrayType());
				il.Emit(OpCodes.Stloc, instance); // instance = (type[])arg_1;
				
				// for (int i = 0; i < instance.Length; i++) read &instance[i];
				
				var loopStart = il.DefineLabel();
				var loopHead = il.DefineLabel();
				var loopVariable = il.DeclareLocal(typeof(int));
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable = 0
				il.Emit(OpCodes.Br, loopHead); // goto loopHead;
				
				il.MarkLabel(loopStart);
				
				if (type.IsEnum || type.IsPrimitive) {
					if (type.IsEnum) {
						type = type.GetEnumUnderlyingType();
					}
					Debug.Assert(type.IsPrimitive);
					il.Emit(OpCodes.Ldloc, instance); // instance
					il.Emit(OpCodes.Ldloc, loopVariable); // instance, loopVariable
					EmitReadValueType(il, reader, type); // instance, loopVariable, value
					switch (Type.GetTypeCode(type)) {
						case TypeCode.Boolean:
						case TypeCode.SByte:
						case TypeCode.Byte:
							il.Emit(OpCodes.Stelem_I1); // instance[loopVariable] = value;
							break;
						case TypeCode.Char:
						case TypeCode.Int16:
						case TypeCode.UInt16:
							il.Emit(OpCodes.Stelem_I2); // instance[loopVariable] = value;
							break;
						case TypeCode.Int32:
						case TypeCode.UInt32:
							il.Emit(OpCodes.Stelem_I4); // instance[loopVariable] = value;
							break;
						case TypeCode.Int64:
						case TypeCode.UInt64:
							il.Emit(OpCodes.Stelem_I8); // instance[loopVariable] = value;
							break;
						case TypeCode.Single:
							il.Emit(OpCodes.Stelem_R4); // instance[loopVariable] = value;
							break;
						case TypeCode.Double:
							il.Emit(OpCodes.Stelem_R8); // instance[loopVariable] = value;
							break;
						default:
							throw new NotSupportedException("Unknown primitive type " + type);
					}
				} else {
					il.Emit(OpCodes.Ldloc, instance); // instance
					il.Emit(OpCodes.Ldloc, loopVariable); // instance, loopVariable
					il.Emit(OpCodes.Ldelema, type); // instance[loopVariable]
					EmitReadValueType(il, reader, type);
				}
				
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldc_I4_1); // loopVariable, 1
				il.Emit(OpCodes.Add); // loopVariable+1
				il.Emit(OpCodes.Stloc, loopVariable); // loopVariable++;
				
				il.MarkLabel(loopHead);
				il.Emit(OpCodes.Ldloc, loopVariable); // loopVariable
				il.Emit(OpCodes.Ldloc, instance); // loopVariable, instance
				il.Emit(OpCodes.Ldlen); // loopVariable, instance.Length
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Blt, loopStart); // if (loopVariable < instance.Length) goto loopStart;
			} else if (type.IsValueType) {
				// boxed value type
				il.Emit(OpCodes.Ldarg_1); // instance
				il.Emit(OpCodes.Unbox, type); // &(Type)instance
				if (type.IsEnum || type.IsPrimitive) {
					if (type.IsEnum) {
						type = type.GetEnumUnderlyingType();
					}
					Debug.Assert(type.IsPrimitive);
					ReadPrimitiveValue(il, reader, type); // &(Type)instance, value
					switch (Type.GetTypeCode(type)) {
						case TypeCode.Boolean:
						case TypeCode.SByte:
						case TypeCode.Byte:
							il.Emit(OpCodes.Stind_I1);
							break;
						case TypeCode.Char:
						case TypeCode.Int16:
						case TypeCode.UInt16:
							il.Emit(OpCodes.Stind_I2);
							break;
						case TypeCode.Int32:
						case TypeCode.UInt32:
							il.Emit(OpCodes.Stind_I4);
							break;
						case TypeCode.Int64:
						case TypeCode.UInt64:
							il.Emit(OpCodes.Stind_I8);
							break;
						case TypeCode.Single:
							il.Emit(OpCodes.Stind_R4);
							break;
						case TypeCode.Double:
							il.Emit(OpCodes.Stind_R8);
							break;
						default:
							throw new NotSupportedException("Unknown primitive type " + type);
					}
				} else {
					EmitReadValueType(il, reader, type);
				}
			} else {
				// reference type
				var instance = il.DeclareLocal(type);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, type);
				il.Emit(OpCodes.Stloc, instance); // instance = (type)arg_1;
				
				foreach (FieldInfo field in fields) {
					EmitReadField(il, reader, instance, field); // read instance.Field
				}
			}
			il.Emit(OpCodes.Ret);
			return (ObjectReader)dynamicMethod.CreateDelegate(typeof(ObjectReader));
		}

		void EmitReadField(ILGenerator il, LocalBuilder reader, LocalBuilder instance, FieldInfo field)
		{
			Type fieldType = field.FieldType;
			if (fieldType.IsValueType) {
				if (fieldType.IsPrimitive || fieldType.IsEnum) {
					il.Emit(OpCodes.Ldloc, instance); // instance
					ReadPrimitiveValue(il, reader, fieldType); // instance, value
					il.Emit(OpCodes.Stfld, field); // instance.field = value;
				} else {
					il.Emit(OpCodes.Ldloc, instance); // instance
					il.Emit(OpCodes.Ldflda, field); // &instance.field
					EmitReadValueType(il, reader, fieldType);
				}
			} else {
				il.Emit(OpCodes.Ldloc, instance); // instance
				il.Emit(OpCodes.Ldarg_0); // instance, context
				il.Emit(OpCodes.Call, readObject); // instance, context.ReadObject()
				il.Emit(OpCodes.Stfld, field); // instance.field = context.ReadObject();
			}
		}

		/// <summary>
		/// Reads a primitive value of the specified type.
		/// Stack transition: ... => ..., value
		/// </summary>
		void ReadPrimitiveValue(ILGenerator il, LocalBuilder reader, Type fieldType)
		{
			if (fieldType.IsEnum) {
				fieldType = fieldType.GetEnumUnderlyingType();
				Debug.Assert(fieldType.IsPrimitive);
			}
			il.Emit(OpCodes.Ldloc, reader);
			switch (Type.GetTypeCode(fieldType)) {
				case TypeCode.Boolean:
				case TypeCode.SByte:
				case TypeCode.Byte:
					il.Emit(callVirt, readByte);
					break;
				case TypeCode.Char:
				case TypeCode.Int16:
				case TypeCode.UInt16:
					il.Emit(callVirt, readShort);
					break;
				case TypeCode.Int32:
				case TypeCode.UInt32:
					il.Emit(callVirt, readInt);
					break;
				case TypeCode.Int64:
				case TypeCode.UInt64:
					il.Emit(callVirt, readLong);
					break;
				case TypeCode.Single:
					il.Emit(callVirt, readFloat);
					break;
				case TypeCode.Double:
					il.Emit(callVirt, readDouble);
					break;
				default:
					throw new NotSupportedException("Unknown primitive type " + fieldType);
			}
		}

		/// <summary>
		/// Stack transition: ..., field-ref => ...
		/// </summary>
		void EmitReadValueType(ILGenerator il, LocalBuilder reader, Type valType)
		{
			Debug.Assert(valType.IsValueType);
			Debug.Assert(!(valType.IsEnum || valType.IsPrimitive));
			
			var fieldRef = il.DeclareLocal(valType.MakeByRefType());
			il.Emit(OpCodes.Stloc, fieldRef);
			
			foreach (FieldInfo field in GetSerializableFields(valType)) {
				EmitReadField(il, reader, fieldRef, field);
			}
		}
		#endregion
		
		#region Custom Deserialization
		struct CustomDeserialization
		{
			readonly object instance;
			readonly SerializationInfo serializationInfo;
			readonly CustomDeserializationAction action;
			
			public CustomDeserialization(object instance, SerializationInfo serializationInfo, CustomDeserializationAction action)
			{
				this.instance = instance;
				this.serializationInfo = serializationInfo;
				this.action = action;
			}
			
			public void Run(StreamingContext context)
			{
				action(instance, serializationInfo, context);
			}
		}
		
		delegate void CustomDeserializationAction(object instance, SerializationInfo info, StreamingContext context);
		
		Dictionary<Type, CustomDeserializationAction> customDeserializationActions = new Dictionary<Type, CustomDeserializationAction>();
		
		CustomDeserializationAction GetCustomDeserializationAction(Type type)
		{
			CustomDeserializationAction action;
			if (!customDeserializationActions.TryGetValue(type, out action)) {
				action = CreateCustomDeserializationAction(type);
				customDeserializationActions.Add(type, action);
			}
			return action;
		}
		
		CustomDeserializationAction CreateCustomDeserializationAction(Type type)
		{
			ConstructorInfo ctor = type.GetConstructor(
				BindingFlags.DeclaredOnly | BindingFlags.ExactBinding | BindingFlags.Instance
				| BindingFlags.NonPublic | BindingFlags.Public,
				null,
				new Type [] { typeof(SerializationInfo), typeof(StreamingContext) },
				null);
			if (ctor == null)
				throw new SerializationException("Could not find deserialization constructor for " + type.FullName);
			
			DynamicMethod dynamicMethod = new DynamicMethod(
				"CallCtor_" + type.Name,
				MethodAttributes.Public | MethodAttributes.Static,
				CallingConventions.Standard,
				typeof(void), new [] { typeof(object), typeof(SerializationInfo), typeof(StreamingContext) },
				type,
				true);
			ILGenerator il = dynamicMethod.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldarg_2);
			il.Emit(OpCodes.Call, ctor);
			il.Emit(OpCodes.Ret);
			return (CustomDeserializationAction)dynamicMethod.CreateDelegate(typeof(CustomDeserializationAction));
		}
		#endregion
		#endregion
		
		[Conditional("DEBUG_SERIALIZER")]
		static void Log(string format, params object[] args)
		{
			Debug.WriteLine(format, args);
		}
	}
}
