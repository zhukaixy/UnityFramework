using UnityEngine;
using System;

namespace Framework
{
	using DynamicValueSystem;

	namespace NodeGraphSystem
	{
		[NodeCategory("Quaternions")]
		[Serializable]
		public class QuaternionEulerNode : Node, IValueSource<Quaternion>
		{
			#region Public Data	
			public NodeInputField<Vector3> _euler = Vector3.zero;
			#endregion

			#region IValueSource<Quaternion>
			public Quaternion GetValue()
			{
				return Quaternion.Euler(_euler);
			}

#if UNITY_EDITOR
			public override Color GetEditorColor()
			{
				return QuaternionNodes.kNodeColor;
			}
#endif
			#endregion
		}
	}
}