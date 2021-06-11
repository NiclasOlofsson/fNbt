using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace fNbt.Serialization
{
	public static class NbtSerializer
	{
		private const BindingFlags MemberBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

		public static NbtCompound SerializeObject(object value)
		{
			return (NbtCompound)SerializeChild(null, value);
		}

		public static T DeserializeObject<T>(NbtTag tag)
		{
			return (T)DeserializeChild(typeof(T), tag);
		}

		public static void FillObject<T>(T value, NbtTag tag) where T : class
		{
			FillObject(value, value.GetType(), tag);
		}

		private static NbtTag SerializeChild(string name, object value)
		{
			if (value is NbtTag normalValue)
			{
				if (name != null) normalValue.Name = name;
				return normalValue;
			}

			var tag = CreateBaseTag(name, value);
			if (tag != null) return tag;

			if (value is IList list)
			{
				return GetNbtList(name, list);
			}
			else if (value is IDictionary dictionary)
			{
				return GetNbtCompaund(name, dictionary);
			}

			var type = value.GetType();

			var properties = type.GetProperties(MemberBindingFlags);
			var fields = type.GetFields(MemberBindingFlags);

			if (properties.Length == 0 && fields.Length == 0) return null;

			var nbt = new NbtCompound();
			if (name != null) nbt.Name = name;

			foreach (var property in properties)
			{
				var child = SerializeMember(property, property.GetValue(value));
				if (child != null) nbt.Add(child);
			}

			foreach (var filed in fields)
			{
				var child = SerializeMember(filed, filed.GetValue(value));
				if (child != null) nbt.Add(child);
			}

			if (nbt.Count == 0) return null;

			return nbt;
		}

		private static NbtTag SerializeMember(MemberInfo memberInfo, object value)
		{
			var attribute = GetAttribute(memberInfo);
			if (attribute == null) return null;

			if (attribute.HideDefault && value.Equals(GetDefaultValue(value))) return null;

			string childName = attribute.Name ?? memberInfo.Name;
			return SerializeChild(childName, value);
		}

		public static object GetDefaultValue(object value)
		{
			return value switch
			{
				byte or sbyte or
				short or short or
				int or uint or
				long or ulong or
				decimal or double or float => 0,
				bool => false,
				_ => null
			};
		}

		private static object DeserializeChild(Type type, NbtTag tag)
		{
			if (typeof(NbtTag).IsAssignableFrom(type))
			{
				tag.Name = null;
				return tag;
			}

			var value = GetValueFromTag(tag, type);
			if (value != null) return value;

			if (typeof(IList).IsAssignableFrom(type))
			{
				return GetList(type, (NbtList)tag);
			}
			else if (typeof(IDictionary).IsAssignableFrom(type))
			{
				return GetDictionary(type, (NbtCompound)tag);
			}

			value = Activator.CreateInstance(type);

			DeserializeBase(value, type, tag);

			return value;
		}

		private static void DeserializeBase(object value, Type type, NbtTag tag)
		{
			var compound = (NbtCompound)tag;

			var properties = type.GetProperties();
			var fields = type.GetFields();

			if (compound.Count == 0) return;

			foreach (var property in properties)
			{
				if (!TryGetMemberTag(property, compound, out NbtTag child)) continue;

				if (property.SetMethod == null)
				{
					FillObject(property.GetValue(value), property.PropertyType, child);
					continue;
				}

				property.SetValue(value, DeserializeChild(property.PropertyType, child));
			}

			foreach (var filed in fields)
			{
				if (!TryGetMemberTag(filed, compound, out NbtTag child)) continue;
				filed.SetValue(value, DeserializeChild(filed.FieldType, child));
			}
		}

		private static bool TryGetMemberTag(MemberInfo memberInfo, NbtCompound compound, out NbtTag tag)
		{
			tag = null;

			var attribute = GetAttribute(memberInfo);
			if (attribute == null) return false;

			string childName = attribute.Name ?? memberInfo.Name;
			return compound.TryGet(childName, out tag);
		}

		private static void FillObject(object value, Type type, NbtTag tag)
		{
			var baseTypeValue = GetValueFromTag(tag, type);
			if (baseTypeValue != null) return;

			if (value is IList list)
			{
				list.Clear();
				FillList(list, list.GetType(), (NbtList)tag);
				return;
			}
			else if (value is IDictionary dictionary)
			{
				dictionary.Clear();
				FillDictionary(dictionary, dictionary.GetType(), (NbtCompound)tag);
				return;
			}

			DeserializeBase(value, type, tag);
		}

		private static NbtPropertyAttribute GetAttribute(MemberInfo memberInfo)
		{
			return memberInfo.GetCustomAttribute<NbtPropertyAttribute>();
		}

		private static NbtTag? CreateBaseTag(string name, object value)
		{
			return value switch
			{
				byte _value => new NbtByte(name, _value),
				sbyte _value => new NbtByte(name, (byte)_value),
				short _value => new NbtShort(name, _value),
				ushort _value => new NbtShort(name, (short)_value),
				int _value => new NbtInt(name, _value),
				uint _value => new NbtInt(name, (int)_value),
				long _value => new NbtLong(name, _value),
				ulong _value => new NbtLong(name, (long)_value),
				double _value => new NbtDouble(name, _value),
				float _value => new NbtFloat(name, _value),
				string _value => new NbtString(name, _value),
				byte[] _value => new NbtByteArray(name, _value),
				int[] _value => new NbtIntArray(name, _value),
				_ => null
			};
		}

		private static object? GetValueFromTag(NbtTag tag, Type type)
		{
			return (tag) switch
			{
				NbtByte _value => type == typeof(byte) ? _value.Value : (sbyte)_value.Value,
				NbtShort _value => type == typeof(short) ? _value.Value : (ushort)_value.Value,
				NbtInt _value => type == typeof(int) ? _value.Value : (uint)_value.Value,
				NbtLong _value => type == typeof(long) ? _value.Value : (ulong)_value.Value,
				NbtDouble _value => _value.Value,
				NbtFloat _value => _value.Value,
				NbtString _value => _value.Value,
				NbtByteArray _value => _value.Value,
				NbtIntArray _value => _value.Value,
				_ => null
			};
		}

		private static NbtList GetNbtList(string name, IList list)
		{
			if (list.Count == 0) return null;

			var nbt = new NbtList();
			if (name != null) nbt.Name = name;

			foreach (var value in list)
			{
				nbt.Add(SerializeChild(null, value));
			}

			return nbt;
		}

		private static IList GetList(Type type, NbtList tag)
		{
			var list = (IList)Activator.CreateInstance(type);

			FillList(list, type, tag);
			return list;
		}

		private static void FillList(IList list, Type type, NbtList tag)
		{
			if (tag.Count == 0) return;

			var listType = type.GetGenericArguments().First();

			foreach (var child in tag)
			{
				list.Add(DeserializeChild(listType, child));
			}
		}

		private static NbtCompound GetNbtCompaund(string name, IDictionary dictionary)
		{
			if (dictionary.Count == 0) return null;
			if (dictionary.GetType().GetGenericArguments().First() != typeof(string)) return null;

			var keys = dictionary.Keys.GetEnumerator();
			var values = dictionary.Values.GetEnumerator();

			var nbt = new NbtCompound();
			if (name != null) nbt.Name = name;

			while (keys.MoveNext() && values.MoveNext())
			{
				var childName = (string)keys.Current;
				nbt.Add(SerializeChild(childName, values.Current));
			}

			return nbt;
		}

		private static IDictionary GetDictionary(Type type, NbtCompound tag)
		{
			var dictionary = (IDictionary)Activator.CreateInstance(type);

			FillDictionary(dictionary, type, tag);
			return dictionary;
		}

		private static void FillDictionary(IDictionary dictionary, Type type, NbtCompound tag)
		{
			var dictionaryType = type.GetGenericArguments().Last();

			foreach (var child in tag)
			{
				dictionary.Add(child.Name, DeserializeChild(dictionaryType, child));
			}
		}
	}
}
