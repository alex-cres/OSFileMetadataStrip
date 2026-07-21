using System;
using System.Collections;
using System.Data;
using System.Reflection;
using System.Runtime.Serialization;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;

namespace OutSystems.NssFileMetadataStripping {

	/// <summary>
	/// Structure <code>STFileMetadataResultStructure</code> that represents the Service Studio structure
	///  <code>FileMetadataResult</code> <p> Description: Result of stripping metadata from a file
	/// , including the clean file and the metadata that was removed for policy review.</p>
	/// </summary>
	[Serializable()]
	public partial struct STFileMetadataResultStructure: ISerializable, ITypedRecord<STFileMetadataResultStructure>, ISimpleRecord {
		internal static readonly GlobalObjectKey IdCleanFile = GlobalObjectKey.Parse("lw7Ks96ViU20pGkAyy2ZGA*m7aLiTotMUeuNVGukXRtfQ");
		internal static readonly GlobalObjectKey IdExtractedMetadata = GlobalObjectKey.Parse("lw7Ks96ViU20pGkAyy2ZGA*UMoPXk0kBkyYf1xnK0koYw");
		internal static readonly GlobalObjectKey IdRemovedEntryCount = GlobalObjectKey.Parse("lw7Ks96ViU20pGkAyy2ZGA*lT0_xnhZGkaotXz07Rf_IA");
		internal static readonly GlobalObjectKey IdIsPassthrough = GlobalObjectKey.Parse("lw7Ks96ViU20pGkAyy2ZGA*N1kWjGSutUyQ31MCqSI65A");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("CleanFile")]
		public byte[] ssCleanFile;

		[System.Xml.Serialization.XmlElement("ExtractedMetadata")]
		public string ssExtractedMetadata;

		[System.Xml.Serialization.XmlElement("RemovedEntryCount")]
		public int ssRemovedEntryCount;

		[System.Xml.Serialization.XmlElement("IsPassthrough")]
		public bool ssIsPassthrough;


		public BitArray OptimizedAttributes;

		public STFileMetadataResultStructure(params string[] dummy) {
			OptimizedAttributes = null;
			ssCleanFile = new byte[] {};
			ssExtractedMetadata = "";
			ssRemovedEntryCount = 0;
			ssIsPassthrough = false;
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[0];
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
				}
			}
			get {
				BitArray[] all = new BitArray[0];
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssCleanFile = r.ReadBinaryData(index++, "FileMetadataResult.CleanFile", new byte[] {});
			ssExtractedMetadata = r.ReadText(index++, "FileMetadataResult.ExtractedMetadata", "");
			ssRemovedEntryCount = r.ReadInteger(index++, "FileMetadataResult.RemovedEntryCount", 0);
			ssIsPassthrough = r.ReadBoolean(index++, "FileMetadataResult.IsPassthrough", false);
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
		public void ReadIM(STFileMetadataResultStructure r) {
			this = r;
		}


		public static bool operator == (STFileMetadataResultStructure a, STFileMetadataResultStructure b) {
			if (!RuntimePlatformUtils.CompareByteArrays(a.ssCleanFile, b.ssCleanFile)) return false;
			if (a.ssExtractedMetadata != b.ssExtractedMetadata) return false;
			if (a.ssRemovedEntryCount != b.ssRemovedEntryCount) return false;
			if (a.ssIsPassthrough != b.ssIsPassthrough) return false;
			return true;
		}

		public static bool operator != (STFileMetadataResultStructure a, STFileMetadataResultStructure b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(STFileMetadataResultStructure)) return false;
			return (this == (STFileMetadataResultStructure) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssCleanFile.GetHashCode()
				^ ssExtractedMetadata.GetHashCode()
				^ ssRemovedEntryCount.GetHashCode()
				^ ssIsPassthrough.GetHashCode()
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

		public STFileMetadataResultStructure(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssCleanFile = new byte[] {};
			ssExtractedMetadata = "";
			ssRemovedEntryCount = 0;
			ssIsPassthrough = false;
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssCleanFile", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssCleanFile' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssCleanFile = (byte[]) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssExtractedMetadata", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssExtractedMetadata' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssExtractedMetadata = (string) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssRemovedEntryCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssRemovedEntryCount' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssRemovedEntryCount = (int) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssIsPassthrough", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssIsPassthrough' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssIsPassthrough = (bool) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
		}

		public void InternalRecursiveSave() {
		}


		public STFileMetadataResultStructure Duplicate() {
			STFileMetadataResultStructure t;
			if (this.ssCleanFile != null) {
				t.ssCleanFile = (byte[]) this.ssCleanFile.Clone();
			} else {
				t.ssCleanFile = null;
			}
			t.ssExtractedMetadata = this.ssExtractedMetadata;
			t.ssRemovedEntryCount = this.ssRemovedEntryCount;
			t.ssIsPassthrough = this.ssIsPassthrough;
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Structure");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
				fieldName = fieldName.ToLowerInvariant();
			}
			if (detailLevel > 0) {
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".CleanFile")) VarValue.AppendAttribute(recordElem, "CleanFile", ssCleanFile, detailLevel, TypeKind.BinaryData); else VarValue.AppendOptimizedAttribute(recordElem, "CleanFile");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".ExtractedMetadata")) VarValue.AppendAttribute(recordElem, "ExtractedMetadata", ssExtractedMetadata, detailLevel, TypeKind.Text); else VarValue.AppendOptimizedAttribute(recordElem, "ExtractedMetadata");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".RemovedEntryCount")) VarValue.AppendAttribute(recordElem, "RemovedEntryCount", ssRemovedEntryCount, detailLevel, TypeKind.Integer); else VarValue.AppendOptimizedAttribute(recordElem, "RemovedEntryCount");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".IsPassthrough")) VarValue.AppendAttribute(recordElem, "IsPassthrough", ssIsPassthrough, detailLevel, TypeKind.Boolean); else VarValue.AppendOptimizedAttribute(recordElem, "IsPassthrough");
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "cleanfile") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".CleanFile")) variable.Value = ssCleanFile; else variable.Optimized = true;
			} else if (head == "extractedmetadata") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".ExtractedMetadata")) variable.Value = ssExtractedMetadata; else variable.Optimized = true;
			} else if (head == "removedentrycount") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".RemovedEntryCount")) variable.Value = ssRemovedEntryCount; else variable.Optimized = true;
			} else if (head == "ispassthrough") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".IsPassthrough")) variable.Value = ssIsPassthrough; else variable.Optimized = true;
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
			if (key == IdCleanFile) {
				return ssCleanFile;
			} else if (key == IdExtractedMetadata) {
				return ssExtractedMetadata;
			} else if (key == IdRemovedEntryCount) {
				return ssRemovedEntryCount;
			} else if (key == IdIsPassthrough) {
				return ssIsPassthrough;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssCleanFile = (byte[]) other.AttributeGet(IdCleanFile);
			ssExtractedMetadata = (string) other.AttributeGet(IdExtractedMetadata);
			ssRemovedEntryCount = (int) other.AttributeGet(IdRemovedEntryCount);
			ssIsPassthrough = (bool) other.AttributeGet(IdIsPassthrough);
		}
		public bool IsDefault() {
			STFileMetadataResultStructure defaultStruct = new STFileMetadataResultStructure(null);
			if (!RuntimePlatformUtils.CompareByteArrays(this.ssCleanFile, defaultStruct.ssCleanFile)) return false;
			if (this.ssExtractedMetadata != defaultStruct.ssExtractedMetadata) return false;
			if (this.ssRemovedEntryCount != defaultStruct.ssRemovedEntryCount) return false;
			if (this.ssIsPassthrough != defaultStruct.ssIsPassthrough) return false;
			return true;
		}
	} // STFileMetadataResultStructure

} // OutSystems.NssFileMetadataStripping
