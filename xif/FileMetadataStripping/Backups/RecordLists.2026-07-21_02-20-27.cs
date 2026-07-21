using System;
using System.Data;
using System.Collections;
using System.Runtime.Serialization;
using System.Reflection;
using System.Xml;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;
using OutSystems.HubEdition.RuntimePlatform.NewRuntime;

namespace OutSystems.NssFileMetadataStripping {

	/// <summary>
	/// RecordList type <code>RLFileMetadataResultRecordList</code> that represents a record list of
	///  <code>FileMetadataResult</code>
	/// </summary>
	[Serializable()]
	public partial class RLFileMetadataResultRecordList: GenericRecordList<RCFileMetadataResultRecord>, IEnumerable, IEnumerator, ISerializable {
		public static void EnsureInitialized() {}

		protected override RCFileMetadataResultRecord GetElementDefaultValue() {
			return new RCFileMetadataResultRecord("");
		}

		public T[] ToArray<T>(Func<RCFileMetadataResultRecord, T> converter) {
			return ToArray(this, converter);
		}

		public static T[] ToArray<T>(RLFileMetadataResultRecordList recordlist, Func<RCFileMetadataResultRecord, T> converter) {
			return InnerToArray(recordlist, converter);
		}
		public static implicit operator RLFileMetadataResultRecordList(RCFileMetadataResultRecord[] array) {
			RLFileMetadataResultRecordList result = new RLFileMetadataResultRecordList();
			result.InnerFromArray(array);
			return result;
		}

		public static RLFileMetadataResultRecordList ToList<T>(T[] array, Func <T, RCFileMetadataResultRecord> converter) {
			RLFileMetadataResultRecordList result = new RLFileMetadataResultRecordList();
			result.InnerFromArray(array, converter);
			return result;
		}

		public static RLFileMetadataResultRecordList FromRestList<T>(RestList<T> restList, Func <T, RCFileMetadataResultRecord> converter) {
			RLFileMetadataResultRecordList result = new RLFileMetadataResultRecordList();
			result.InnerFromRestList(restList, converter);
			return result;
		}
		/// <summary>
		/// Default Constructor
		/// </summary>
		public RLFileMetadataResultRecordList(): base() {
		}

		/// <summary>
		/// Constructor with transaction parameter
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLFileMetadataResultRecordList(IDbTransaction trans): base(trans) {
		}

		/// <summary>
		/// Constructor with transaction parameter and alternate read method
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		/// <param name="alternateReadDBMethod"> Alternate Read Method</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLFileMetadataResultRecordList(IDbTransaction trans, ReadDBMethodDelegate alternateReadDBMethod): this(trans) {
			this.alternateReadDBMethod = alternateReadDBMethod;
		}

		/// <summary>
		/// Constructor declaration for serialization
		/// </summary>
		/// <param name="info"> SerializationInfo</param>
		/// <param name="context"> StreamingContext</param>
		public RLFileMetadataResultRecordList(SerializationInfo info, StreamingContext context): base(info, context) {
		}

		public override BitArray[] GetDefaultOptimizedValues() {
			BitArray[] def = new BitArray[1];
			def[0] = null;
			return def;
		}
		/// <summary>
		/// Create as new list
		/// </summary>
		/// <returns>The new record list</returns>
		protected override OSList<RCFileMetadataResultRecord> NewList() {
			return new RLFileMetadataResultRecordList();
		}


	} // RLFileMetadataResultRecordList
}
