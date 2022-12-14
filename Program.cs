using EasyNetQ;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var func = CreateAssembly();
            Console.ReadKey();
        }

        public static Func<object, object[], object> CreateAssembly()
        {
            //var _reflectionInfo = typeof(JsonSerializer).GetMethod("BytesToMessage");
            var _reflectionInfo = typeof(ISerializer).GetMethod("BytesToMessage");
            DynamicMethod dynamicMethod = new DynamicMethod($"invoker_Object BytesToMessageKlw(Type,Int32&)",
               typeof(object), new Type[] { typeof(object), typeof(object[]) }, _reflectionInfo.Module, true);
            ILGenerator ilGen = dynamicMethod.GetILGenerator();
            var parameterTypes = _reflectionInfo.GetParameterTypes();
            ilGen.EmitLoadArg(0);
            ilGen.EmitConvertFromObject(_reflectionInfo.DeclaringType);
            if (parameterTypes.Length == 0)
            {
                return CreateDelegate();
            }

            var refParameterCount = parameterTypes.Count(x => x.IsByRef);
            if (refParameterCount == 0)
            {
                for (var i = 0; i < parameterTypes.Length; i++)
                {
                    ilGen.EmitLoadArg(1);
                    ilGen.EmitInt(i);
                    ilGen.Emit(OpCodes.Ldelem_Ref);
                    ilGen.EmitConvertFromObject(parameterTypes[i]);
                }
                return CreateDelegate();
            }

            var indexedLocals = new IndexedLocalBuilder[refParameterCount];
            var index = 0;
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                ilGen.EmitLoadArg(1);
                ilGen.EmitInt(i);
                ilGen.Emit(OpCodes.Ldelem_Ref);
                if (parameterTypes[i].IsByRef)
                {
                    var defType = parameterTypes[i].GetElementType();
                    var indexedLocal = new IndexedLocalBuilder(ilGen.DeclareLocal(defType), i);
                    indexedLocals[index++] = indexedLocal;
                    ilGen.EmitConvertFromObject(defType);
                    ilGen.Emit(OpCodes.Stloc, indexedLocal.LocalBuilder);
                    ilGen.Emit(OpCodes.Ldloca, indexedLocal.LocalBuilder);
                }
                else
                {
                    ilGen.EmitConvertFromObject(parameterTypes[i]);
                }
            }

            return CreateDelegate(() =>
            {
                for (var i = 0; i < indexedLocals.Length; i++)
                {
                    ilGen.EmitLoadArg(1);
                    ilGen.EmitInt(indexedLocals[i].Index);
                    ilGen.Emit(OpCodes.Ldloc, indexedLocals[i].LocalBuilder);
                    ilGen.EmitConvertToObject(indexedLocals[i].LocalType);
                    ilGen.Emit(OpCodes.Stelem_Ref);
                }
            });
            Func<object, object[], object> CreateDelegate(Action callback = null)
            {
                ilGen.EmitCall(OpCodes.Callvirt, _reflectionInfo, null);
                callback?.Invoke();
                if (_reflectionInfo.ReturnType == typeof(void)) ilGen.Emit(OpCodes.Ldnull);
                else if (_reflectionInfo.ReturnType.GetTypeInfo().IsValueType)
                    ilGen.EmitConvertToObject(_reflectionInfo.ReturnType);
                ilGen.Emit(OpCodes.Ret);
                return (Func<object, object[], object>)dynamicMethod.CreateDelegate(typeof(Func<object, object[], object>));
            }
        }
    }


    public struct IndexedLocalBuilder
    {
        public LocalBuilder LocalBuilder { get; }
        public Type LocalType { get; }
        public int Index { get; }
        public int LocalIndex { get; }

        public IndexedLocalBuilder(LocalBuilder localBuilder, int index)
        {
            LocalBuilder = localBuilder;
            LocalType = localBuilder.LocalType;
            LocalIndex = localBuilder.LocalIndex;
            Index = index;
        }
    }

    public static class ILGeneratorExtensions
    {
        public static void EmitLoadArg(this ILGenerator ilGenerator, int index)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }

            switch (index)
            {
                case 0:
                    ilGenerator.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    ilGenerator.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    ilGenerator.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    ilGenerator.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    if (index <= byte.MaxValue) ilGenerator.Emit(OpCodes.Ldarg_S, (byte)index);
                    else ilGenerator.Emit(OpCodes.Ldarg, index);
                    break;
            }
        }

        public static void EmitConvertToObject(this ILGenerator ilGenerator, Type typeFrom)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (typeFrom == null)
            {
                throw new ArgumentNullException(nameof(typeFrom));
            }

            if (typeFrom.GetTypeInfo().IsGenericParameter)
            {
                ilGenerator.Emit(OpCodes.Box, typeFrom);
            }
            else
            {
                ilGenerator.EmitConvertToType(typeFrom, typeof(object), true);
            }
        }

        public static void EmitConvertFromObject(this ILGenerator ilGenerator, Type typeTo)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (typeTo == null)
            {
                throw new ArgumentNullException(nameof(typeTo));
            }

            if (typeTo.GetTypeInfo().IsGenericParameter)
            {
                ilGenerator.Emit(OpCodes.Unbox_Any, typeTo);
            }
            else
            {
                ilGenerator.EmitConvertToType(typeof(object), typeTo, true);
            }
        }

        public static void EmitType(this ILGenerator ilGenerator, Type type)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            ilGenerator.Emit(OpCodes.Ldtoken, type);
            ilGenerator.Emit(OpCodes.Call, MethodInfoConstant.GetTypeFromHandle);
        }

        public static void EmitMethod(this ILGenerator ilGenerator, MethodInfo method)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            EmitMethod(ilGenerator, method, method.DeclaringType);
        }

        public static void EmitMethod(this ILGenerator ilGenerator, MethodInfo method, Type declaringType)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            ilGenerator.Emit(OpCodes.Ldtoken, method);
            ilGenerator.Emit(OpCodes.Ldtoken, method.DeclaringType);
            ilGenerator.Emit(OpCodes.Call, MethodInfoConstant.GetMethodFromHandle);
            ilGenerator.EmitConvertToType(typeof(MethodBase), typeof(MethodInfo));
        }

        public static void EmitConvertToType(this ILGenerator ilGenerator, Type typeFrom, Type typeTo, bool isChecked = true)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (typeFrom == null)
            {
                throw new ArgumentNullException(nameof(typeFrom));
            }
            if (typeTo == null)
            {
                throw new ArgumentNullException(nameof(typeTo));
            }

            var typeFromInfo = typeFrom.GetTypeInfo();
            var typeToInfo = typeTo.GetTypeInfo();

            var nnExprType = typeFromInfo.GetNonNullableType();
            var nnType = typeToInfo.GetNonNullableType();

            if (TypeInfoUtils.AreEquivalent(typeFromInfo, typeToInfo))
            {
                return;
            }

            if (typeFromInfo.IsInterface || // interface cast
              typeToInfo.IsInterface ||
               typeFrom == typeof(object) || // boxing cast
               typeTo == typeof(object) ||
               typeFrom == typeof(System.Enum) ||
               typeFrom == typeof(System.ValueType) ||
               TypeInfoUtils.IsLegalExplicitVariantDelegateConversion(typeFromInfo, typeToInfo))
            {
                ilGenerator.EmitCastToType(typeFromInfo, typeToInfo);
            }
            else if (typeFromInfo.IsNullableType() || typeToInfo.IsNullableType())
            {
                ilGenerator.EmitNullableConversion(typeFromInfo, typeToInfo, isChecked);
            }
            else if (!(typeFromInfo.IsConvertible() && typeToInfo.IsConvertible()) // primitive runtime conversion
                     &&
                     (nnExprType.GetTypeInfo().IsAssignableFrom(nnType) || // down cast
                     nnType.GetTypeInfo().IsAssignableFrom(nnExprType))) // up cast
            {
                ilGenerator.EmitCastToType(typeFromInfo, typeToInfo);
            }
            else if (typeFromInfo.IsArray && typeToInfo.IsArray)
            {
                // See DevDiv Bugs #94657.
                ilGenerator.EmitCastToType(typeFromInfo, typeToInfo);
            }
            else
            {
                ilGenerator.EmitNumericConversion(typeFromInfo, typeToInfo, isChecked);
            }
        }

        public static void EmitCastToType(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (!typeFrom.IsValueType && typeTo.IsValueType)
            {
                ilGenerator.Emit(OpCodes.Unbox_Any, typeTo.AsType());
            }
            else if (typeFrom.IsValueType && !typeTo.IsValueType)
            {
                ilGenerator.Emit(OpCodes.Box, typeFrom.AsType());
                if (typeTo.AsType() != typeof(object))
                {
                    ilGenerator.Emit(OpCodes.Castclass, typeTo.AsType());
                }
            }
            else if (!typeFrom.IsValueType && !typeTo.IsValueType)
            {
                ilGenerator.Emit(OpCodes.Castclass, typeTo.AsType());
            }
            else
            {
                throw new InvalidCastException($"Caanot cast {typeFrom} to {typeTo}.");
            }
        }

        public static void EmitHasValue(this ILGenerator ilGenerator, Type nullableType)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            MethodInfo mi = nullableType.GetTypeInfo().GetMethod("get_HasValue", BindingFlags.Instance | BindingFlags.Public);
            ilGenerator.Emit(OpCodes.Call, mi);
        }

        public static void EmitGetValueOrDefault(this ILGenerator ilGenerator, Type nullableType)
        {
            MethodInfo mi = nullableType.GetTypeInfo().GetMethod("GetValueOrDefault", Type.EmptyTypes);
            ilGenerator.Emit(OpCodes.Call, mi);
        }

        public static void EmitGetValue(this ILGenerator ilGenerator, Type nullableType)
        {
            MethodInfo mi = nullableType.GetTypeInfo().GetMethod("get_Value", BindingFlags.Instance | BindingFlags.Public);
            ilGenerator.Emit(OpCodes.Call, mi);
        }

        public static void EmitConstant(this ILGenerator ilGenerator, object value, Type valueType)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (valueType == null)
            {
                throw new ArgumentNullException(nameof(valueType));
            }
            if (value == null)
            {
                EmitDefault(ilGenerator, valueType);
                return;
            }

            if (ilGenerator.TryEmitILConstant(value, valueType))
            {
                return;
            }

            var t = value as Type;
            if (t != null)
            {
                ilGenerator.EmitType(t);
                if (valueType != typeof(Type))
                {
                    ilGenerator.Emit(OpCodes.Castclass, valueType);
                }
                return;
            }

            var mb = value as MethodBase;
            if (mb != null)
            {
                ilGenerator.EmitMethod((MethodInfo)mb);
                return;
            }

            if (valueType.GetTypeInfo().IsArray)
            {
                var array = (Array)value;
                ilGenerator.EmitArray(array, valueType.GetElementType());
            }

            throw new InvalidOperationException("Code supposed to be unreachable.");
        }

        public static void EmitDefault(this ILGenerator ilGenerator, Type type)
        {
            if (ilGenerator == null)
            {
                throw new ArgumentNullException(nameof(ilGenerator));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Object:
                case TypeCode.DateTime:
                    if (type.GetTypeInfo().IsValueType)
                    {
                        // Type.GetTypeCode on an enum returns the underlying
                        // integer TypeCode, so we won't get here.
                        // This is the IL for default(T) if T is a generic type
                        // parameter, so it should work for any type. It's also
                        // the standard pattern for structs.
                        LocalBuilder lb = ilGenerator.DeclareLocal(type);
                        ilGenerator.Emit(OpCodes.Ldloca, lb);
                        ilGenerator.Emit(OpCodes.Initobj, type);
                        ilGenerator.Emit(OpCodes.Ldloc, lb);
                    }
                    else
                    {
                        ilGenerator.Emit(OpCodes.Ldnull);
                    }
                    break;

                case TypeCode.Empty:
                case TypeCode.String:
                    ilGenerator.Emit(OpCodes.Ldnull);
                    break;

                case TypeCode.Boolean:
                case TypeCode.Char:
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    ilGenerator.Emit(OpCodes.Ldc_I4_0);
                    break;

                case TypeCode.Int64:
                case TypeCode.UInt64:
                    ilGenerator.Emit(OpCodes.Ldc_I4_0);
                    ilGenerator.Emit(OpCodes.Conv_I8);
                    break;

                case TypeCode.Single:
                    ilGenerator.Emit(OpCodes.Ldc_R4, default(Single));
                    break;

                case TypeCode.Double:
                    ilGenerator.Emit(OpCodes.Ldc_R8, default(Double));
                    break;

                case TypeCode.Decimal:
                    ilGenerator.Emit(OpCodes.Ldc_I4_0);
                    ilGenerator.Emit(OpCodes.Newobj, typeof(Decimal).GetTypeInfo().GetConstructor(new Type[] { typeof(int) }));
                    break;

                default:
                    throw new InvalidOperationException("Code supposed to be unreachable.");
            }
        }

        public static void EmitDecimal(this ILGenerator ilGenerator, decimal value)
        {
            if (Decimal.Truncate(value) == value)
            {
                if (Int32.MinValue <= value && value <= Int32.MaxValue)
                {
                    int intValue = Decimal.ToInt32(value);
                    ilGenerator.EmitInt(intValue);
                    ilGenerator.EmitNew(typeof(Decimal).GetTypeInfo().GetConstructor(new Type[] { typeof(int) }));
                }
                else if (Int64.MinValue <= value && value <= Int64.MaxValue)
                {
                    long longValue = Decimal.ToInt64(value);
                    ilGenerator.EmitLong(longValue);
                    ilGenerator.EmitNew(typeof(Decimal).GetTypeInfo().GetConstructor(new Type[] { typeof(long) }));
                }
                else
                {
                    ilGenerator.EmitDecimalBits(value);
                }
            }
            else
            {
                ilGenerator.EmitDecimalBits(value);
            }
        }

        public static void EmitNew(this ILGenerator ilGenerator, ConstructorInfo ci)
        {
            ilGenerator.Emit(OpCodes.Newobj, ci);
        }

        public static void EmitString(this ILGenerator ilGenerator, string value)
        {
            ilGenerator.Emit(OpCodes.Ldstr, value);
        }

        public static void EmitBoolean(this ILGenerator ilGenerator, bool value)
        {
            if (value)
            {
                ilGenerator.Emit(OpCodes.Ldc_I4_1);
            }
            else
            {
                ilGenerator.Emit(OpCodes.Ldc_I4_0);
            }
        }

        public static void EmitChar(this ILGenerator ilGenerator, char value)
        {
            ilGenerator.EmitInt(value);
            ilGenerator.Emit(OpCodes.Conv_U2);
        }

        public static void EmitByte(this ILGenerator ilGenerator, byte value)
        {
            ilGenerator.EmitInt(value);
            ilGenerator.Emit(OpCodes.Conv_U1);
        }

        public static void EmitSByte(this ILGenerator ilGenerator, sbyte value)
        {
            ilGenerator.EmitInt(value);
            ilGenerator.Emit(OpCodes.Conv_I1);
        }

        public static void EmitShort(this ILGenerator ilGenerator, short value)
        {
            ilGenerator.EmitInt(value);
            ilGenerator.Emit(OpCodes.Conv_I2);
        }

        public static void EmitUShort(this ILGenerator ilGenerator, ushort value)
        {
            ilGenerator.EmitInt(value);
            ilGenerator.Emit(OpCodes.Conv_U2);
        }

        public static void EmitInt(this ILGenerator ilGenerator, int value)
        {
            OpCode c;
            switch (value)
            {
                case -1:
                    c = OpCodes.Ldc_I4_M1;
                    break;
                case 0:
                    c = OpCodes.Ldc_I4_0;
                    break;
                case 1:
                    c = OpCodes.Ldc_I4_1;
                    break;
                case 2:
                    c = OpCodes.Ldc_I4_2;
                    break;
                case 3:
                    c = OpCodes.Ldc_I4_3;
                    break;
                case 4:
                    c = OpCodes.Ldc_I4_4;
                    break;
                case 5:
                    c = OpCodes.Ldc_I4_5;
                    break;
                case 6:
                    c = OpCodes.Ldc_I4_6;
                    break;
                case 7:
                    c = OpCodes.Ldc_I4_7;
                    break;
                case 8:
                    c = OpCodes.Ldc_I4_8;
                    break;
                default:
                    if (value >= -128 && value <= 127)
                    {
                        ilGenerator.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                    }
                    else
                    {
                        ilGenerator.Emit(OpCodes.Ldc_I4, value);
                    }
                    return;
            }
            ilGenerator.Emit(c);
        }

        public static void EmitUInt(this ILGenerator ilGenerator, uint value)
        {
            ilGenerator.EmitInt((int)value);
            ilGenerator.Emit(OpCodes.Conv_U4);
        }

        public static void EmitLong(this ILGenerator ilGenerator, long value)
        {
            ilGenerator.Emit(OpCodes.Ldc_I8, value);
            ilGenerator.Emit(OpCodes.Conv_I8);
        }

        public static void EmitULong(this ILGenerator ilGenerator, ulong value)
        {
            ilGenerator.Emit(OpCodes.Ldc_I8, (long)value);
            ilGenerator.Emit(OpCodes.Conv_U8);
        }

        public static void EmitDouble(this ILGenerator ilGenerator, double value)
        {
            ilGenerator.Emit(OpCodes.Ldc_R8, value);
        }

        public static void EmitSingle(this ILGenerator ilGenerator, float value)
        {
            ilGenerator.Emit(OpCodes.Ldc_R4, value);
        }

        public static void EmitArray(this ILGenerator ilGenerator, Array items, Type elementType)
        {
            ilGenerator.EmitInt(items.Length);
            ilGenerator.Emit(OpCodes.Newarr, elementType);
            for (int i = 0; i < items.Length; i++)
            {
                ilGenerator.Emit(OpCodes.Dup);
                ilGenerator.EmitInt(i);
                //ilGenerator.EmitConstant(items.GetValue(i), elementType);
                var constantType = items.GetValue(i).GetType();
                if (constantType == elementType)
                {
                    ilGenerator.EmitConstant(items.GetValue(i), elementType);
                }
                else
                {
                    ilGenerator.EmitConstant(items.GetValue(i), constantType);
                    ilGenerator.EmitConvertToObject(constantType);
                }
                ilGenerator.EmitStoreElement(elementType);
            }
        }

        public static void EmitStoreElement(this ILGenerator ilGenerator, Type type)
        {
            if (type.GetTypeInfo().IsEnum)
            {
                ilGenerator.Emit(OpCodes.Stelem, type);
                return;
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                case TypeCode.SByte:
                case TypeCode.Byte:
                    ilGenerator.Emit(OpCodes.Stelem_I1);
                    break;
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    ilGenerator.Emit(OpCodes.Stelem_I2);
                    break;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    ilGenerator.Emit(OpCodes.Stelem_I4);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    ilGenerator.Emit(OpCodes.Stelem_I8);
                    break;
                case TypeCode.Single:
                    ilGenerator.Emit(OpCodes.Stelem_R4);
                    break;
                case TypeCode.Double:
                    ilGenerator.Emit(OpCodes.Stelem_R8);
                    break;
                default:
                    if (type.GetTypeInfo().IsValueType)
                    {
                        ilGenerator.Emit(OpCodes.Stelem, type);
                    }
                    else
                    {
                        ilGenerator.Emit(OpCodes.Stelem_Ref);
                    }
                    break;
            }
        }
        #region private

        private static void EmitNullableConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            bool isTypeFromNullable = TypeInfoUtils.IsNullableType(typeFrom);
            bool isTypeToNullable = TypeInfoUtils.IsNullableType(typeTo);
            if (isTypeFromNullable && isTypeToNullable)
                ilGenerator.EmitNullableToNullableConversion(typeFrom, typeTo, isChecked);
            else if (isTypeFromNullable)
                ilGenerator.EmitNullableToNonNullableConversion(typeFrom, typeTo, isChecked);
            else
                ilGenerator.EmitNonNullableToNullableConversion(typeFrom, typeTo, isChecked);
        }

        private static void EmitNullableToNullableConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            LocalBuilder locFrom = ilGenerator.DeclareLocal(typeFrom.AsType());
            ilGenerator.Emit(OpCodes.Stloc, locFrom);
            LocalBuilder locTo = ilGenerator.DeclareLocal(typeTo.AsType());
            // test for null
            ilGenerator.Emit(OpCodes.Ldloca, locFrom);
            ilGenerator.EmitHasValue(typeFrom.AsType());
            Label labIfNull = ilGenerator.DefineLabel();
            ilGenerator.Emit(OpCodes.Brfalse_S, labIfNull);
            ilGenerator.Emit(OpCodes.Ldloca, locFrom);
            ilGenerator.EmitGetValueOrDefault(typeFrom.AsType());
            Type nnTypeFrom = TypeInfoUtils.GetNonNullableType(typeFrom);
            Type nnTypeTo = TypeInfoUtils.GetNonNullableType(typeTo);
            ilGenerator.EmitConvertToType(nnTypeFrom, nnTypeTo, isChecked);
            // construct result type
            ConstructorInfo ci = typeTo.GetConstructor(new Type[] { nnTypeTo });
            ilGenerator.Emit(OpCodes.Newobj, ci);
            ilGenerator.Emit(OpCodes.Stloc, locTo);
            Label labEnd = ilGenerator.DefineLabel();
            ilGenerator.Emit(OpCodes.Br_S, labEnd);
            // if null then create a default one
            ilGenerator.MarkLabel(labIfNull);
            ilGenerator.Emit(OpCodes.Ldloca, locTo);
            ilGenerator.Emit(OpCodes.Initobj, typeTo.AsType());
            ilGenerator.MarkLabel(labEnd);
            ilGenerator.Emit(OpCodes.Ldloc, locTo);
        }

        private static void EmitNullableToNonNullableConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            if (typeTo.IsValueType)
                ilGenerator.EmitNullableToNonNullableStructConversion(typeFrom, typeTo, isChecked);
            else
                ilGenerator.EmitNullableToReferenceConversion(typeFrom);
        }

        private static void EmitNullableToNonNullableStructConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            LocalBuilder locFrom = ilGenerator.DeclareLocal(typeFrom.AsType());
            ilGenerator.Emit(OpCodes.Stloc, locFrom);
            ilGenerator.Emit(OpCodes.Ldloca, locFrom);
            ilGenerator.EmitGetValue(typeFrom.AsType());
            Type nnTypeFrom = TypeInfoUtils.GetNonNullableType(typeFrom);
            ilGenerator.EmitConvertToType(nnTypeFrom, typeTo.AsType(), isChecked);
        }

        private static void EmitNullableToReferenceConversion(this ILGenerator ilGenerator, TypeInfo typeFrom)
        {
            ilGenerator.Emit(OpCodes.Box, typeFrom.AsType());
        }

        private static void EmitNonNullableToNullableConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            LocalBuilder locTo = ilGenerator.DeclareLocal(typeTo.AsType());
            Type nnTypeTo = TypeInfoUtils.GetNonNullableType(typeTo);
            ilGenerator.EmitConvertToType(typeFrom.AsType(), nnTypeTo, isChecked);
            ConstructorInfo ci = typeTo.GetConstructor(new Type[] { nnTypeTo });
            ilGenerator.Emit(OpCodes.Newobj, ci);
            ilGenerator.Emit(OpCodes.Stloc, locTo);
            ilGenerator.Emit(OpCodes.Ldloc, locTo);
        }

        private static void EmitNumericConversion(this ILGenerator ilGenerator, TypeInfo typeFrom, TypeInfo typeTo, bool isChecked)
        {
            bool isFromUnsigned = TypeInfoUtils.IsUnsigned(typeFrom);
            bool isFromFloatingPoint = TypeInfoUtils.IsFloatingPoint(typeFrom);
            if (typeTo.AsType() == typeof(Single))
            {
                if (isFromUnsigned)
                    ilGenerator.Emit(OpCodes.Conv_R_Un);
                ilGenerator.Emit(OpCodes.Conv_R4);
            }
            else if (typeTo.AsType() == typeof(Double))
            {
                if (isFromUnsigned)
                    ilGenerator.Emit(OpCodes.Conv_R_Un);
                ilGenerator.Emit(OpCodes.Conv_R8);
            }
            else
            {
                TypeCode tc = Type.GetTypeCode(typeTo.AsType());
                if (isChecked)
                {
                    // Overflow checking needs to know if the source value on the IL stack is unsigned or not.
                    if (isFromUnsigned)
                    {
                        switch (tc)
                        {
                            case TypeCode.SByte:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I1_Un);
                                break;
                            case TypeCode.Int16:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I2_Un);
                                break;
                            case TypeCode.Int32:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I4_Un);
                                break;
                            case TypeCode.Int64:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I8_Un);
                                break;
                            case TypeCode.Byte:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U1_Un);
                                break;
                            case TypeCode.UInt16:
                            case TypeCode.Char:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U2_Un);
                                break;
                            case TypeCode.UInt32:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U4_Un);
                                break;
                            case TypeCode.UInt64:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U8_Un);
                                break;
                            default:
                                throw new InvalidCastException();
                        }
                    }
                    else
                    {
                        switch (tc)
                        {
                            case TypeCode.SByte:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I1);
                                break;
                            case TypeCode.Int16:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I2);
                                break;
                            case TypeCode.Int32:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I4);
                                break;
                            case TypeCode.Int64:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_I8);
                                break;
                            case TypeCode.Byte:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U1);
                                break;
                            case TypeCode.UInt16:
                            case TypeCode.Char:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U2);
                                break;
                            case TypeCode.UInt32:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U4);
                                break;
                            case TypeCode.UInt64:
                                ilGenerator.Emit(OpCodes.Conv_Ovf_U8);
                                break;
                            default:
                                throw new InvalidCastException();
                        }
                    }
                }
                else
                {
                    switch (tc)
                    {
                        case TypeCode.SByte:
                            ilGenerator.Emit(OpCodes.Conv_I1);
                            break;
                        case TypeCode.Byte:
                            ilGenerator.Emit(OpCodes.Conv_U1);
                            break;
                        case TypeCode.Int16:
                            ilGenerator.Emit(OpCodes.Conv_I2);
                            break;
                        case TypeCode.UInt16:
                        case TypeCode.Char:
                            ilGenerator.Emit(OpCodes.Conv_U2);
                            break;
                        case TypeCode.Int32:
                            ilGenerator.Emit(OpCodes.Conv_I4);
                            break;
                        case TypeCode.UInt32:
                            ilGenerator.Emit(OpCodes.Conv_U4);
                            break;
                        case TypeCode.Int64:
                            if (isFromUnsigned)
                            {
                                ilGenerator.Emit(OpCodes.Conv_U8);
                            }
                            else
                            {
                                ilGenerator.Emit(OpCodes.Conv_I8);
                            }
                            break;
                        case TypeCode.UInt64:
                            if (isFromUnsigned || isFromFloatingPoint)
                            {
                                ilGenerator.Emit(OpCodes.Conv_U8);
                            }
                            else
                            {
                                ilGenerator.Emit(OpCodes.Conv_I8);
                            }
                            break;
                        default:
                            throw new InvalidCastException();
                    }
                }
            }
        }

        private static bool TryEmitILConstant(this ILGenerator ilGenerator, object value, Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    ilGenerator.EmitBoolean((bool)value);
                    return true;
                case TypeCode.SByte:
                    ilGenerator.EmitSByte((sbyte)value);
                    return true;
                case TypeCode.Int16:
                    ilGenerator.EmitShort((short)value);
                    return true;
                case TypeCode.Int32:
                    ilGenerator.EmitInt((int)value);
                    return true;
                case TypeCode.Int64:
                    ilGenerator.EmitLong((long)value);
                    return true;
                case TypeCode.Single:
                    ilGenerator.EmitSingle((float)value);
                    return true;
                case TypeCode.Double:
                    ilGenerator.EmitDouble((double)value);
                    return true;
                case TypeCode.Char:
                    ilGenerator.EmitChar((char)value);
                    return true;
                case TypeCode.Byte:
                    ilGenerator.EmitByte((byte)value);
                    return true;
                case TypeCode.UInt16:
                    ilGenerator.EmitUShort((ushort)value);
                    return true;
                case TypeCode.UInt32:
                    ilGenerator.EmitUInt((uint)value);
                    return true;
                case TypeCode.UInt64:
                    ilGenerator.EmitULong((ulong)value);
                    return true;
                case TypeCode.Decimal:
                    ilGenerator.EmitDecimal((decimal)value);
                    return true;
                case TypeCode.String:
                    ilGenerator.EmitString((string)value);
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitDecimalBits(this ILGenerator ilGenerator, decimal value)
        {
            int[] bits = Decimal.GetBits(value);
            ilGenerator.EmitInt(bits[0]);
            ilGenerator.EmitInt(bits[1]);
            ilGenerator.EmitInt(bits[2]);
            ilGenerator.EmitBoolean((bits[3] & 0x80000000) != 0);
            ilGenerator.EmitByte((byte)(bits[3] >> 16));
            ilGenerator.EmitNew(typeof(decimal).GetTypeInfo().GetConstructor(new Type[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(byte) }));
        }
        #endregion
    }

    internal static class MethodInfoConstant
    {
        internal static readonly MethodInfo GetTypeFromHandle = InternalExtensions.GetMethod<Func<RuntimeTypeHandle, Type>>(handle => Type.GetTypeFromHandle(handle));

        internal static readonly MethodInfo GetMethodFromHandle = InternalExtensions.GetMethod<Func<RuntimeMethodHandle, RuntimeTypeHandle, MethodBase>>((h1, h2) => MethodBase.GetMethodFromHandle(h1, h2));

        internal static readonly ConstructorInfo ArgumentNullExceptionCtor = typeof(ArgumentNullException).GetTypeInfo().GetConstructor(new Type[] { typeof(string) });

        internal static readonly ConstructorInfo ObjectCtor = typeof(object).GetTypeInfo().DeclaredConstructors.Single();
    }

    internal static class InternalExtensions
    {
        internal static MethodInfo GetMethod<T>(Expression<T> expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }
            var methodCallExpression = expression.Body as MethodCallExpression;
            if (methodCallExpression == null)
            {
                throw new InvalidCastException("Cannot be converted to MethodCallExpression");
            }
            return methodCallExpression.Method;
        }

        internal static Type[] GetParameterTypes(this MethodBase method)
        {
            return method.GetParameters().Select(x => x.ParameterType).ToArray();
        }
    }

    internal static class TypeInfoUtils
    {
        internal static bool AreEquivalent(TypeInfo t1, TypeInfo t2)
        {
            return t1 == t2 || t1.IsEquivalentTo(t2.AsType());
        }

        internal static bool IsNullableType(this TypeInfo type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        internal static Type GetNonNullableType(this TypeInfo type)
        {
            if (IsNullableType(type))
            {
                return type.GetGenericArguments()[0];
            }
            return type.AsType();
        }

        internal static bool IsLegalExplicitVariantDelegateConversion(TypeInfo source, TypeInfo dest)
        {
            if (!IsDelegate(source) || !IsDelegate(dest) || !source.IsGenericType || !dest.IsGenericType)
                return false;

            var genericDelegate = source.GetGenericTypeDefinition();

            if (dest.GetGenericTypeDefinition() != genericDelegate)
                return false;

            var genericParameters = genericDelegate.GetTypeInfo().GetGenericArguments();
            var sourceArguments = source.GetGenericArguments();
            var destArguments = dest.GetGenericArguments();

            for (int iParam = 0; iParam < genericParameters.Length; ++iParam)
            {
                var sourceArgument = sourceArguments[iParam].GetTypeInfo();
                var destArgument = destArguments[iParam].GetTypeInfo();

                if (AreEquivalent(sourceArgument, destArgument))
                {
                    continue;
                }

                var genericParameter = genericParameters[iParam].GetTypeInfo();

                if (IsInvariant(genericParameter))
                {
                    return false;
                }

                if (IsCovariant(genericParameter))
                {
                    if (!HasReferenceConversion(sourceArgument, destArgument))
                    {
                        return false;
                    }
                }
                else if (IsContravariant(genericParameter))
                {
                    if (sourceArgument.IsValueType || destArgument.IsValueType)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsDelegate(TypeInfo t)
        {
            return t.IsSubclassOf(typeof(System.MulticastDelegate));
        }

        private static bool IsInvariant(TypeInfo t)
        {
            return 0 == (t.GenericParameterAttributes & GenericParameterAttributes.VarianceMask);
        }

        private static bool IsCovariant(this TypeInfo t)
        {
            return 0 != (t.GenericParameterAttributes & GenericParameterAttributes.Covariant);
        }

        internal static bool HasReferenceConversion(TypeInfo source, TypeInfo dest)
        {
            if (source.AsType() == typeof(void) || dest.AsType() == typeof(void))
            {
                return false;
            }

            var nnSourceType = TypeInfoUtils.GetNonNullableType(source).GetTypeInfo();
            var nnDestType = TypeInfoUtils.GetNonNullableType(dest).GetTypeInfo();

            // Down conversion
            if (nnSourceType.IsAssignableFrom(nnDestType))
            {
                return true;
            }
            // Up conversion
            if (nnDestType.IsAssignableFrom(nnSourceType))
            {
                return true;
            }
            // Interface conversion
            if (source.IsInterface || dest.IsInterface)
            {
                return true;
            }
            // Variant delegate conversion
            if (IsLegalExplicitVariantDelegateConversion(source, dest))
                return true;

            // Object conversion
            if (source.AsType() == typeof(object) || dest.AsType() == typeof(object))
            {
                return true;
            }
            return false;
        }

        private static bool IsContravariant(TypeInfo t)
        {
            return 0 != (t.GenericParameterAttributes & GenericParameterAttributes.Contravariant);
        }

        internal static bool IsConvertible(this TypeInfo typeInfo)
        {
            var type = GetNonNullableType(typeInfo);
            if (typeInfo.IsEnum)
            {
                return true;
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Char:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsUnsigned(TypeInfo typeInfo)
        {
            var type = GetNonNullableType(typeInfo);
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.Char:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsFloatingPoint(TypeInfo typeInfo)
        {
            var type = GetNonNullableType(typeInfo);
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Single:
                case TypeCode.Double:
                    return true;
                default:
                    return false;
            }
        }
    }
}