using System;
using System.Collections;
using System.Data;
using System.Runtime.Serialization;
using System.Reflection;
using System.Xml;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;

namespace OutSystems.NssFileMetadataStripping {

	/// <summary>
	/// Structure <code>RCFileMetadataResultRecord</code>
	/// </summary>
	[Serializable()]
	public partial struct RCFileMetadataResultRecord: ISerializable, ITypedRecord<RCFileMetadataResultRecord> {
		internal static readonly GlobalObjectKey IdFileMetadataResult = GlobalObjectKey.Parse("2UmDmepsh0WSfJ_D1JexCA*9erSGk4acjbQEreojC8H6g");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("FileMetadataResult")]
		public STFileMetadataResultStructure ssSTFileMetadataResult;


		public static implicit operator STFileMetadataResultStructure(RCFileMetadataResultRecord r) {
			return r.ssSTFileMetadataResult;
		}

		public static implicit operator RCFileMetadataResultRecord(STFileMetadataResultStructure r) {
			RCFileMetadataResultRecord res = new RCFileMetadataResultRecord(null);
			res.ssSTFileMetadataResult = r;
			return res;
		}

		public BitArray OptimizedAttributes;

		public RCFileMetadataResultRecord(params string[] dummy) {
			OptimizedAttributes = null;
			ssSTFileMetadataResult = new STFileMetadataResultStructure(null);
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[1];
			all[0] = null;
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
					ssSTFileMetadataResult.OptimizedAttributes = value[0];
				}
			}
			get {
				BitArray[] all = new BitArray[1];
				all[0] = null;
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssSTFileMetadataResult.Read(r, ref index);
		}
		/// <summary>
		/// Read from database
		/// </summary>
		/// <param name="r"> Data reader</param>
		public void ReadDB(IDataReader r) {
			int index = 0;
			Read(r, ref index);
		}

		/// <summary>
		/// Read from record
		/// </summary>
		/// <param name="r"> Record</param>
		public void ReadIM(RCFileMetadataResultRecord r) {
			this = r;
		}


		public static bool operator == (RCFileMetadataResultRecord a, RCFileMetadataResultRecord b) {
			if (a.ssSTFileMetadataResult != b.ssSTFileMetadataResult) return false;
			return true;
		}

		public static bool operator != (RCFileMetadataResultRecord a, RCFileMetadataResultRecord b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(RCFileMetadataResultRecord)) return false;
			return (this == (RCFileMetadataResultRecord) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssSTFileMetadataResult.GetHashCode()
				;
			} catch {
				return base.GetHashCode();
			}
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) {
			Type objInfo = this.GetType();
			FieldInfo[] fields;
			fields = objInfo.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < fields.Length; i++)
			if (fields[i] .FieldType.IsSerializable)
			info.AddValue(fields[i] .Name, fields[i] .GetValue(this));
		}

		public RCFileMetadataResultRecord(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssSTFileMetadataResult = new STFileMetadataResultStructure(null);
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssSTFileMetadataResult", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssSTFileMetadataResult' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssSTFileMetadataResult = (STFileMetadataResultStructure) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
			ssSTFileMetadataResult.RecursiveReset();
		}

		public void InternalRecursiveSave() {
			ssSTFileMetadataResult.InternalRecursiveSave();
		}


		public RCFileMetadataResultRecord Duplicate() {
			RCFileMetadataResultRecord t;
			t.ssSTFileMetadataResult = (STFileMetadataResultStructure) this.ssSTFileMetadataResult.Duplicate();
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Record");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
			}
			if (detailLevel > 0) {
				ssSTFileMetadataResult.ToXml(this, recordElem, "FileMetadataResult", detailLevel - 1);
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "filemetadataresult") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".FileMetadataResult")) variable.Value = ssSTFileMetadataResult; else variable.Optimized = true;
				variable.SetFieldName("filemetadataresult");
			}
			if (variable.Found && tail != null) variable.EvaluateFields(this, head, tail);
		}

		public bool ChangedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public bool OptimizedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public object AttributeGet(GlobalObjectKey key) {
			if (key == IdFileMetadataResult) {
				return ssSTFileMetadataResult;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssSTFileMetadataResult.FillFromOther((IRecord) other.AttributeGet(IdFileMetadataResult));
		}
		public bool IsDefault() {
			RCFileMetadataResultRecord defaultStruct = new RCFileMetadataResultRecord(null);
			if (this.ssSTFileMetadataResult != defaultStruct.ssSTFileMetadataResult) return false;
			return true;
		}
	} // RCFileMetadataResultRecord
}
