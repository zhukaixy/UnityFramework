﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Framework
{
	using Maths;	
	using Utils;
	
	namespace MeshInstancing
	{
		public class SkinnedMeshInstanceSpawner : MonoBehaviour
		{
			#region Public Data
			public AnimationTextureRef _animationTexture;
			public PrefabResourceRef _prefab;
			public bool _sortByDepth;

			public enum eFrustrumCulling
			{
				Off,
				Sphere,
				Bounds
			}
			public eFrustrumCulling _frustrumCulling;
			public float _sphereCullingRadius;

			[Serializable]
			public struct Animation
			{
				public int _animationIndex;
				[Range(0, 1)]
				public float _probability;
				public FloatRange _speedRange;
			}
			
			[HideInInspector]
			public Animation[] _animations;
			#endregion

			#region Private Data
			protected struct InstanceData
			{
				public GameObject _gameObject;
				public int _animationIndex;
				public float _currentFrame;
				public float _animationSpeed;
				public SkinnedMeshRenderer[] _skinnedMeshes;
				public object _extraData;
			}
			private InstanceData[] _instanceData;
			private float[] _currentFrame;
			private SkinnedMeshRenderer[] _skinnedMeshes;
			private MaterialPropertyBlock _propertyBlock;
			private class RenderData
			{
				public int _index;
				public Matrix4x4 _transform;
				public float _zDist;
			}
			private RenderData[] _renderData;
			private List<RenderData> _renderedObjects;
			private Matrix4x4[] _renderedObjectTransforms;
			private GameObject _referencePrefab;
			private static readonly int kMaxMeshes = 1023;
			#endregion

			#region Monobehaviour
			private void Awake()
			{
				//Find list of meshes and materials from prefab
				_referencePrefab = _prefab.LoadAndInstantiatePrefab(this.transform);
				_referencePrefab.SetActive(false);

				if (_referencePrefab != null)
				{
					_skinnedMeshes = _referencePrefab.GetComponentsInChildren<SkinnedMeshRenderer>();

					for (int i=0; i<_skinnedMeshes.Length; i++)
					{
						_skinnedMeshes[i].sharedMesh = AnimationTexture.AddExtraMeshData(_skinnedMeshes[i].sharedMesh);

						for (int j = 0; j < _skinnedMeshes[i].sharedMaterials.Length; j++)
						{
							_animationTexture.SetMaterialProperties(_skinnedMeshes[i].sharedMaterials[j]);
						}
					}
				}

				_renderedObjects = new List<RenderData>(kMaxMeshes);
				_instanceData = new InstanceData[kMaxMeshes];
				_renderData = new RenderData[kMaxMeshes];
				for (int i = 0; i < _renderData.Length; i++)
					_renderData[i] = new RenderData();
				_renderedObjectTransforms = new Matrix4x4[kMaxMeshes];
				_currentFrame = new float[kMaxMeshes];

				_propertyBlock = new MaterialPropertyBlock();
			}

			private void Update()
			{
				UpdateAnimations();
				Render(Camera.main);
			}
			#endregion

			#region Public Interface
			public GameObject SpawnObject()
			{
				for (int i = 0; i < _instanceData.Length; i++)
				{
					if (_instanceData[i]._gameObject == null || !_instanceData[i]._gameObject.activeInHierarchy)
					{
						if (_instanceData[i]._gameObject == null)
						{
							_instanceData[i]._gameObject = _prefab.LoadAndInstantiatePrefab(this.transform);

							_instanceData[i]._skinnedMeshes = _instanceData[i]._gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();

							for (int j = 0; j < _instanceData[i]._skinnedMeshes.Length; j++)
								_instanceData[i]._skinnedMeshes[j].enabled = false;
						}

						Animation animation = PickRandomAnimation();
						AnimationTexture.Animation textureAnim = GetAnimation(animation._animationIndex);
						_instanceData[i]._animationIndex = animation._animationIndex;
						_instanceData[i]._currentFrame = Random.Range(0, textureAnim._totalFrames - 2);
						_instanceData[i]._animationSpeed = animation._speedRange.GetRandomValue();

						OnSpawnObject(ref _instanceData[i]);

						return _instanceData[i]._gameObject;
					}
				}

				return null;
			}
			#endregion

			#region Protected Functions
			protected void Render(Camera camera)
			{
				if (_skinnedMeshes == null || _instanceData == null)
					return;

				_renderedObjects.Clear();

				Plane[] planes = null;

				if (_frustrumCulling != eFrustrumCulling.Off)
					planes = GeometryUtility.CalculateFrustumPlanes(camera);

				for (int i = 0; i < _instanceData.Length; i++)
				{
					bool rendered = _instanceData[i]._gameObject != null && _instanceData[i]._gameObject.activeInHierarchy;

					//If frustum culling is enabled, check should draw this game object
					if (rendered && _frustrumCulling == eFrustrumCulling.Bounds)
					{
						rendered = AreBoundsInFrustrum(planes, ref _instanceData[i]);
					}
					else if (rendered && _frustrumCulling == eFrustrumCulling.Sphere)
					{
						Vector3 position = _instanceData[i]._gameObject.transform.position;
						Vector3 scale = _instanceData[i]._gameObject.transform.lossyScale;
						rendered = MathUtils.IsSphereInFrustrum(ref planes, ref position, _sphereCullingRadius * Mathf.Max(scale.x, scale.y, scale.z));
					}

					if (rendered)
					{
						_renderData[i]._index = i;
						_renderData[i]._transform = GetTransform(ref _instanceData[i]);

						if (_sortByDepth)
						{
							Vector3 position = new Vector3(_renderData[i]._transform.m03, _renderData[i]._transform.m13, _renderData[i]._transform.m23);
							_renderData[i]._zDist = (camera.transform.position - position).sqrMagnitude;
						}

						AddToSortedList(ref _renderData[i]);
					}
				}

				if (_renderedObjects.Count > 0)
				{
					UpdateProperties();
					FillTransformMatricies();
					
					for (int i = 0; i < _skinnedMeshes.Length; i++)
					{
						for (int j = 0; j < _skinnedMeshes[i].sharedMesh.subMeshCount; j++)
						{
							Graphics.DrawMeshInstanced(_skinnedMeshes[i].sharedMesh, j, _skinnedMeshes[i].sharedMaterials[j], _renderedObjectTransforms, _renderedObjects.Count, _propertyBlock);
						}
					}
				}
			}

			protected virtual Matrix4x4 GetTransform(ref InstanceData instanceData)
			{
				return instanceData._gameObject.transform.localToWorldMatrix;
			}

			protected virtual void UpdateProperties()
			{
#if UNITY_EDITOR
				//In editor always set shared data
				for (int i = 0; i < _skinnedMeshes.Length; i++)
				{
					for (int j = 0; j < _skinnedMeshes[i].sharedMesh.subMeshCount; j++)
					{
						_animationTexture.SetMaterialProperties(_skinnedMeshes[i].materials[j]);
					}
				}
#endif

				//Update property block
				int index = 0;
				foreach (RenderData renderData in _renderedObjects)
				{
					_currentFrame[index++] = _instanceData[renderData._index]._currentFrame;
				}

				//Update property block
				_propertyBlock.SetFloatArray("frameIndex", _currentFrame);
			}

			protected void UpdateAnimations()
			{
				//Update particle frame progress
				for (int i = 0; i < _instanceData.Length; i++)
				{
					if (_instanceData[i]._gameObject != null && _instanceData[i]._gameObject.activeInHierarchy)
					{
						float prevFrame = _instanceData[i]._currentFrame;

						//Progress current animation
						AnimationTexture.Animation animation = GetAnimation(_instanceData[i]._animationIndex);

						//Update current frame
						_instanceData[i]._currentFrame += Time.deltaTime * animation._fps * _instanceData[i]._animationSpeed;

						//Is animation finished?
						if (Mathf.FloorToInt(_instanceData[i]._currentFrame) >= animation._totalFrames - 2)
						{
							Animation newAnimation = PickRandomAnimation();

							_instanceData[i]._animationIndex = newAnimation._animationIndex;
							_instanceData[i]._currentFrame = 0f;
							_instanceData[i]._animationSpeed = newAnimation._speedRange.GetRandomValue();
						}

						UpdateGameObject(ref _instanceData[i]);
					}
				}
			}

			protected virtual void UpdateGameObject(ref InstanceData instanceData)
			{

			}

			protected virtual void OnSpawnObject(ref InstanceData instanceData)
			{

			}
			#endregion

			#region Private Functions
			private bool AreBoundsInFrustrum(Plane[] cameraFrustrumPlanes, ref InstanceData instanceData)
			{
				//Only test first skinned mesh?
				return GeometryUtility.TestPlanesAABB(cameraFrustrumPlanes, instanceData._skinnedMeshes[0].bounds);
			}

			private Animation PickRandomAnimation()
			{
				float totalWeights = 0.0f;

				for (int i = 0; i < _animations.Length; i++)
				{
					totalWeights += _animations[i]._probability;
				}

				float chosen = Random.Range(0.0f, totalWeights);
				totalWeights = 0.0f;

				for (int i = 0; i < _animations.Length; i++)
				{
					totalWeights += _animations[i]._probability;

					if (chosen <= totalWeights)
						return _animations[i];
				}

				return _animations[0];
			}

			private AnimationTexture.Animation GetAnimation(int index)
			{
				AnimationTexture.Animation[] animations = _animationTexture.GetAnimations();
				AnimationTexture.Animation animation = animations[Mathf.Clamp(index, 0, animations.Length)];
				return animation;
			}

			private void FillTransformMatricies()
			{
				for (int i = 0; i < _renderedObjects.Count; i++)
				{
					_renderedObjectTransforms[i] = _renderedObjects[i]._transform;
				}
			}

			private void AddToSortedList(ref RenderData renderData)
			{
				int index = 0;

				if (_sortByDepth)
				{
					index = FindInsertIndex(renderData._zDist, 0, _renderedObjects.Count);
				}

				_renderedObjects.Insert(index, renderData);
			}

			private static readonly int kSearchNodes = 24;

			private int FindInsertIndex(float zDist, int startIndex, int endIndex)
			{
				int searchWidth = endIndex - startIndex;
				int numSearches = Mathf.Min(kSearchNodes, searchWidth);
				int nodesPerSearch = Mathf.FloorToInt(searchWidth / (float)numSearches);

				int currIndex = startIndex;
				int prevIndex = currIndex;

				for (int i = 0; i < numSearches; i++)
				{
					//If this distance is greater than current node its between this and prev node
					if (zDist > _renderedObjects[currIndex]._zDist)
					{
						//If first node or search one node at a time then found our index
						if (i == 0 || nodesPerSearch == 1)
						{
							return currIndex;
						}
						//Otherwise its between this and the previous index
						else
						{
							return FindInsertIndex(zDist, prevIndex, currIndex);
						}
					}

					prevIndex = currIndex;
					currIndex = (i == numSearches - 1) ? endIndex : startIndex + ((i + 1) * nodesPerSearch);
				}

				return endIndex;
			}
			#endregion
		}
	}
}
