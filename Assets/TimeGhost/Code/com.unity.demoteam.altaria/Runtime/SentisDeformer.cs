using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Sentis;
using UnityEngine.Profiling;


namespace Unity.DemoTeam.DigitalHuman
{
    [ExecuteAlways, RequireComponent(typeof(SkinnedMeshRenderer))]
    public class SentisDeformer : MeshInstanceBehaviour
    {
        static class Uniforms
        {
            public static int _NeutralPositionsBuffer;
            public static int _TargetMeshBufferRW;
            public static int _IndexBuffer;
            
            public static int _AdjacencyListOffsetCount;
            public static int _AdjacentTriangleIndices;
            public static int _TriangleCrossProd;
            public static int _TriangleCrossProdRW;

            public static int _DeformPatchBuffer;
            public static int _PatchDataToVertexIndexMappingBuffer;
            public static int _DeformPatchVertexOffset;
            public static int _DeformPatchVertexCount;
            public static int _DeformRemapRange;
            
            public static int _StrideOffsetPosNormTan;
            public static int _TriangleCount;
            public static int _VertexCount;
            
            public static int _DeformationWeight;
            public static int _DeformationWeightMaskInfluence;
            
            public static int _MeshColorStreamBuffer;
            public static int _MeshColorStreamOffsetStride;
        }

        static class Kernels
        {
            public static int kApplyDeformation;
            public static int kCalculateCrossProductPerTriangle;
            public static int kRecalculateNormals;
        }

#if UNITY_EDITOR
        public static List<SentisDeformer> enabledInstances = new List<SentisDeformer>();
#endif

        [NonSerialized] private MeshBuffers meshAssetBuffers;
        [NonSerialized] private MeshAdjacency meshAdjacency;
        [NonSerialized] private SkinnedMeshRenderer smr;
        
        #region CPU Deform Resources
        private Vector3[] resolvedPositions;
        #endregion

        #region GPU Deform Resources
        private static ComputeShader s_computeShader;
        private GraphicsBuffer neutralPositionsBuffer;
        private GraphicsBuffer patchDataToVertexIndexMappingBuffer;
        private GraphicsBuffer adjacentTriangleIndicesOffsetCountBuffer;
        private GraphicsBuffer adjacentTriangleIndicesBuffer;
        private GraphicsBuffer crossProdPerTriangleBuffer;
        private GraphicsBuffer indexBuffer;
        #endregion

        #region Sentis Deformer

        private bool _resourcesInitialized = false;
        private Model _mRuntimeModel;
        private Worker _mWorker;
        private Tensor<float> outputData;
        private Tensor<float> tensorData;
        private ComputeBuffer outputBuffer;
        private Tensor[] _mInputTensors;
        private List<Unity.Sentis.Model.Output> _mOutputNames;

        private List<NativeArray<int>> _mRenderPatches;
        
        private Vector3[] _deltaVertexPositions;

        private NativeArray<int> _mRenderVertexIds;
        private NativeArray<int> _mRenderVertexIdOffsets;
        private NativeArray<int> _mRenderVertexIdLengths;

        enum SentisBackend
        {
            CPU,
            GPU
        };

        private SentisBackend _latestBackend;
        
        [Header("Sentis Deformer Setup")] 
        [SerializeField] private SentisBackend _backend = new SentisBackend();
        public ModelAsset modelAsset;
        public PatchVertexDataScriptableObject _vertexPatchData;
        
        public List<Transform> joints;
        public float minVal = -0.4587218761444092f; //-0.4766841232776642f;
        public float maxVal = 0.27842265367507935f;//0.30062779784202576f;

        [Header("Sentis Deformer Control")]
        public bool altariaClothEnabled = true;
        [Range(0.0f, 1.0f)] public float deformationWeight = 1f;
        [Range(0.0f, 1.0f)] public float deformationWeightMaskInfluence;
        public bool recalculateNormals = false;

        [Header("Sentis Deformer Debug")]
        public bool DebugSentisResource = false;
#endregion
        
        static void InitializeStaticFields<T>(Type type, Func<string, T> construct)
        {
            foreach (var field in type.GetFields())
            {
                field.SetValue(null, construct(field.Name));
            }
        }

        static int GetDispatchGroupsCount(int kernel, uint threadCount)
        {
            s_computeShader.GetKernelThreadGroupSizes(kernel, out var groupX, out var groupY, out var groupZ);
            return (int)((threadCount + groupX - 1) / groupX);
        }

        static void StaticInitialize()
        {
            if (s_computeShader == null)
            {
                s_computeShader = Resources.Load<ComputeShader>("AltariaCS");
                InitializeStaticFields(typeof(Uniforms), (string s) => Shader.PropertyToID(s));
                InitializeStaticFields(typeof(Kernels), (string s) => s_computeShader.FindKernel(s));
            }
        }

        protected override void OnMeshInstanceCreated()
        {
            if (meshAssetBuffers == null)
                meshAssetBuffers = new MeshBuffers(meshAsset);
            else
                meshAssetBuffers.LoadFrom(meshAsset);
            
            meshInstance.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            EnsureGPUResources();
            
        }

        void OnEnable()
        {
            if (smr == null)
                smr = GetComponent<SkinnedMeshRenderer>();
            
#if UNITY_EDITOR
            if (SentisDeformer.enabledInstances.Contains(this) == false)
                SentisDeformer.enabledInstances.Add(this);
#endif
            InitResources();

            _latestBackend = _backend;
            
            RenderPipelineManager.beginContextRendering -= AfterGpuSkinningCallback;
            RenderPipelineManager.beginContextRendering += AfterGpuSkinningCallback;
        }
        
        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= AfterGpuSkinningCallback;
#if UNITY_EDITOR
            SentisDeformer.enabledInstances.Remove(this);
#endif

            if (smr == null || smr.sharedMesh == null || smr.sharedMesh.GetInstanceID() >= 0)
                return;

            FreeResources();
        }

        void LateUpdate()
        {
            if (_latestBackend != _backend)
            {
                FreeResources();
                InitResources();
                _latestBackend = _backend;
            }

            if (!_resourcesInitialized)
            {
                Debug.LogError("Sentis Deformer - Resources not initialized");
                return;
            }
            
            if (_backend == SentisBackend.CPU)
            {
                UpdateMeshBuffers();
                ExecuteAndReadbackSentisCPU();
                ApplyDeformCPU();
            }
            else
            {
                if (_mWorker.backendType != BackendType.GPUCompute)
                {
                    Debug.LogError($"wrong sentis backend, should be: {BackendType.GPUCompute} is: {_mWorker.backendType}");
                    return;
                }
                
                CommandBuffer cmd = CommandBufferPool.Get("Altaria execute GPU block");
                ExecuteSentisGPU(cmd);
                ApplyDeformationGPU(cmd);
                Graphics.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
        
        private void UpdateMeshBuffers()
        {
            if (meshAssetBuffers == null)
                meshAssetBuffers = new MeshBuffers(meshAsset);
            else
                meshAssetBuffers.LoadFrom(meshAsset);
        }
        
        private float Normalize(float x, float min_value, float max_value)
        {
            float result = ((x + 1f) * (max_value - min_value) / 2f) + min_value;
            return result;
        }
        
        /**********************************************************
         * CPU
         *********************************************************/
        public void ApplyDeformCPU()
        {
            ArrayUtils.ResizeChecked(ref resolvedPositions, meshAssetBuffers.vertexCount);
            Array.Copy(meshAssetBuffers.vertexPositions, resolvedPositions, meshAssetBuffers.vertexCount);
            
            if (altariaClothEnabled)
            {
                if (_deltaVertexPositions != null)
                {
                    int colorStream = -1, stride = 0, offset = 0;
                    Mesh.MeshDataArray colorBufferArray = default;
                    NativeArray<uint> colorBuffer = default;

                    if (deformationWeightMaskInfluence > 0f)
                    {
                        colorStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Color);
                        if (colorStream != -1)
                        {
                            colorBufferArray = Mesh.AcquireReadOnlyMeshData(meshInstance);
                            colorBuffer = colorBufferArray[0].GetVertexData<uint>(colorStream);
                            stride = meshInstance.GetVertexBufferStride(colorStream);
                            offset = meshInstance.GetVertexAttributeOffset(VertexAttribute.Color);
                        }
                    }

                    var weight = math.saturate(deformationWeight);
                    var maskInfluence = math.saturate(deformationWeightMaskInfluence);

                    for (int i = 0; i < resolvedPositions.Length; i++)
                    {
                        float maskedDeformationWeight = 1f;
                        if(deformationWeightMaskInfluence > 0f)
                        {
                            uint color = colorBuffer[i * stride + offset];
                            float maskWeight = math.saturate((color >> 24) / 255f);
                            maskedDeformationWeight = math.lerp(maskedDeformationWeight, maskWeight, maskInfluence);
                        }

                        resolvedPositions[i] += _deltaVertexPositions[i] * (weight * maskedDeformationWeight);
                    }
                }
            }

            meshInstance.SilentlySetVertices(resolvedPositions);
        }
        
        void ExecuteAndReadbackSentisCPU()
        {
            if (_mInputTensors.Length == 0 || _mWorker == null)
                return;

            int jntCnt = 0;
            foreach (var tensor in _mInputTensors)
            {
                tensorData = tensor as Tensor<float>;
                if (tensorData == null)
                {
                    Debug.Log("tensorData is null");
                    return;
                }
                
                //Quaternion localRot = Quaternion.Inverse(joints[_parentJointsIndex[jntCnt]].rotation) * joints[jntCnt].rotation;
                
                tensorData.CompleteAllPendingOperations();

                tensorData[0, 0] = joints[jntCnt].localRotation.x;
                tensorData[0, 1] = joints[jntCnt].localRotation.y;
                tensorData[0, 2] = joints[jntCnt].localRotation.z;
                tensorData[0, 3] = joints[jntCnt].localRotation.w;
                
                jntCnt++;
            }

            _mWorker.Schedule(_mInputTensors);
            
            // Create an empty Vec3 to store all out delta vertices
            ArrayUtils.ResizeChecked(ref _deltaVertexPositions, meshAssetBuffers.vertexCount);
            ArrayUtils.ClearChecked(_deltaVertexPositions);

            /* Each vertex patch has been mapped to contain a set of vertices
             that corresponds to the model output.
             Each model output Tensor contains a flat array of n float values, v=vertex
             |v1.x, v1.y, v1.z, v2.x, v2.y, v3.z....|
              
            */
            Profiler.BeginSample("Altaria Read Output");
            for (int patch = 0; patch < _mOutputNames.Count(); patch++)
            {
                outputData = _mWorker.PeekOutput(_mOutputNames[patch].name) as Tensor<float>;
                float[] outputValues = outputData.DownloadToArray();

                if (outputValues.Count() / 3 != _mRenderPatches[patch].Count())
                {
                    Debug.LogFormat("Patch not matching {0} - {1}, {2}", patch, outputValues.Count() / 3, _mRenderPatches[patch].Count());
                    Debug.LogFormat("Patch name: {0}", _mOutputNames[patch].name);
                    return;
                }

                int tensorIndex = 0;
                for (int k = 0; k < _mRenderPatches[patch].Length; k++)
                {
                    int index = _mRenderPatches[patch][k];
                    Vector3 tensorVec;

                    tensorVec = new Vector3(
                        (Normalize(outputValues[tensorIndex], minVal, maxVal)),
                        (Normalize(outputValues[tensorIndex + 1], minVal, maxVal)),
                        (Normalize(outputValues[tensorIndex + 2], minVal, maxVal))
                    );

                    _deltaVertexPositions[index] = tensorVec;
                    tensorIndex += 3;
                }
            }

            Profiler.EndSample();
        }
        
        /**********************************************************
         * GPU
         *********************************************************/
        void ExecuteSentisGPU(CommandBuffer cmd)
        {
            cmd.BeginSample("Altaria - ExecuteSentisGPU");
            if (_mInputTensors.Length == 0 || _mWorker == null)
                return;
            
            cmd.BeginSample("Altaria - ExecuteSentisGPU - Sample Joints");
            NativeArray<float> temp = new NativeArray<float>(4, Allocator.TempJob);
            
            int jntCnt = 0;
            foreach (var tensor in _mInputTensors)
            {
                //Quaternion localRot = Quaternion.Inverse(joints[_parentJointsIndex[jntCnt]].rotation) * joints[jntCnt].rotation;
                
                tensorData = tensor as Tensor<float>;
                if (tensorData == null)
                {
                    Debug.Log("tensorData is null");
                    return;
                }

                temp[0] = joints[jntCnt].localRotation.x;
                temp[1] = joints[jntCnt].localRotation.y;
                temp[2] = joints[jntCnt].localRotation.z;
                temp[3] = joints[jntCnt].localRotation.w;
                
                tensorData.dataOnBackend.Upload(temp, 4);
                
                jntCnt++;
            }
            cmd.EndSample("Altaria - ExecuteSentisGPU - Sample Joints");
            temp.Dispose();
            cmd.BeginSample("Altaria - ExecuteSentisGPU - Execute Worker");
            cmd.ScheduleWorker(_mWorker, _mInputTensors);
            cmd.EndSample("Altaria - ExecuteSentisGPU - Execute Worker");
            cmd.EndSample("Altaria - ExecuteSentisGPU");
        }
        void AfterGpuSkinningCallback(ScriptableRenderContext scriptableRenderContext, List<Camera> cameras)
        {
            if(smr == null || smr.sharedMesh == null) return;
            if (recalculateNormals)
            {
                CommandBuffer cmd = CommandBufferPool.Get("Altaria Recalculate Normals");
                RecalculateNormals(cmd);
                Graphics.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
           
        }
        void RecalculateNormals(CommandBuffer cmd)
        {
            Profiler.BeginSample("Altaria - RecalculateNormals");
            if (crossProdPerTriangleBuffer == null || !crossProdPerTriangleBuffer.IsValid()) return;

            Mesh skinMesh = smr.sharedMesh;
            int positionStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Normal);
    
            if (positionStream != normalStream)
            {
                Debug.LogError(
                    "AltariaDeform requires that the skin has it's positions and normals in the same vertex buffer/stream.");
                return;
            }
    
            using GraphicsBuffer vertexBuffer = smr.GetVertexBuffer();
            if (vertexBuffer == null)
            {
                return;
            }
    
            int[] strideOffsets =
            {
                meshInstance.GetVertexBufferStride(positionStream),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Position),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Normal),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Tangent)
                
            };
            Profiler.EndSample();
            
            cmd.BeginSample("AltariaDeform: Recalculate Normals");
            //calculate normal and area per triangle
            {
                cmd.SetComputeIntParams(s_computeShader, Uniforms._StrideOffsetPosNormTan, strideOffsets);
                cmd.SetComputeIntParam(s_computeShader, Uniforms._VertexCount, meshAdjacency.vertexCount);
                cmd.SetComputeIntParam(s_computeShader, Uniforms._TriangleCount, meshAdjacency.triangleCount);
    
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kCalculateCrossProductPerTriangle, Uniforms._TriangleCrossProdRW, crossProdPerTriangleBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kCalculateCrossProductPerTriangle, Uniforms._TargetMeshBufferRW, vertexBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kCalculateCrossProductPerTriangle, Uniforms._IndexBuffer, indexBuffer);
    
                int groupsX = GetDispatchGroupsCount(Kernels.kCalculateCrossProductPerTriangle, (uint)meshAdjacency.triangleCount);
                cmd.DispatchCompute(s_computeShader, Kernels.kCalculateCrossProductPerTriangle, groupsX, 1, 1);
            }
    
    
            {
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kRecalculateNormals, Uniforms._TriangleCrossProd, crossProdPerTriangleBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kRecalculateNormals, Uniforms._AdjacentTriangleIndices, adjacentTriangleIndicesBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kRecalculateNormals, Uniforms._AdjacencyListOffsetCount, adjacentTriangleIndicesOffsetCountBuffer);
                
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kRecalculateNormals, Uniforms._TargetMeshBufferRW, vertexBuffer);
    
                
                int groupsX = GetDispatchGroupsCount(Kernels.kRecalculateNormals, (uint)meshAdjacency.vertexCount);
                cmd.DispatchCompute(s_computeShader, Kernels.kRecalculateNormals, groupsX, 1, 1);
            }
    
            
            cmd.EndSample("AltariaDeform: Recalculate Normals");
        }
        void ApplyDeformationGPU(CommandBuffer cmd)
        {
            int posStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Normal);
            int tangentStream = meshInstance.GetVertexAttributeStream(VertexAttribute.Tangent);

            if (posStream != normalStream || posStream != tangentStream)
            {
                Debug.Log("AltariaDeform requires mesh to have positions, normals and tangents in same buffer. Will not apply deformation.");
                return;
            }
            
            int[] strideOffsets =
            {
                meshInstance.GetVertexBufferStride(posStream),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Position),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Normal),
                meshInstance.GetVertexAttributeOffset(VertexAttribute.Tangent)
                
            };
            
            cmd.SetComputeIntParam(s_computeShader, Uniforms._VertexCount, meshAssetBuffers.vertexCount);
            cmd.SetComputeIntParams(s_computeShader, Uniforms._StrideOffsetPosNormTan, strideOffsets);
            cmd.SetComputeFloatParams(s_computeShader, Uniforms._DeformRemapRange, minVal, maxVal);
            
            int colorStream = meshInstance. GetVertexAttributeStream(VertexAttribute.Color);
            if (colorStream != -1)
            {
                using var colorBuffer = meshInstance.GetVertexBuffer(colorStream);
                var stride = meshInstance.GetVertexBufferStride(colorStream);
                var offset = meshInstance.GetVertexAttributeOffset(VertexAttribute.Color);

                int[] strideOffset = { offset, stride };
                cmd.SetComputeIntParams(s_computeShader, Uniforms._MeshColorStreamOffsetStride, strideOffset);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kApplyDeformation, Uniforms._MeshColorStreamBuffer, colorBuffer);
            }

            using GraphicsBuffer meshBuffer = meshInstance.GetVertexBuffer(posStream);

            for (int patch = 0; patch < _mOutputNames.Count(); patch++)
            {
                outputData = _mWorker.PeekOutput(_mOutputNames[patch].name) as Tensor<float>;
                
                outputBuffer = ComputeTensorData.Pin(outputData).buffer;

                if (outputBuffer == null)
                {
                    Debug.LogError($"Tensor for patch #{patch} was null. Ignoring...");
                    continue;
                }
                
                int patchVertexCount = _mRenderVertexIdLengths[patch];
                int patchVertexOffset = _mRenderVertexIdOffsets[patch];

                cmd.BeginSample("Altaria Deformation Apply");

                cmd.SetComputeBufferParam(s_computeShader, Kernels.kApplyDeformation, Uniforms._NeutralPositionsBuffer, neutralPositionsBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kApplyDeformation, Uniforms._TargetMeshBufferRW, meshBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kApplyDeformation, Uniforms._DeformPatchBuffer, outputBuffer);
                cmd.SetComputeBufferParam(s_computeShader, Kernels.kApplyDeformation, Uniforms._PatchDataToVertexIndexMappingBuffer, patchDataToVertexIndexMappingBuffer);
                cmd.SetComputeIntParam(s_computeShader,  Uniforms._DeformPatchVertexCount, patchVertexCount);
                cmd.SetComputeIntParam(s_computeShader,  Uniforms._DeformPatchVertexOffset, patchVertexOffset);
                cmd.SetComputeFloatParam(s_computeShader,  Uniforms._DeformationWeight, altariaClothEnabled ? math.saturate(deformationWeight) : 0f);
                cmd.SetComputeFloatParam(s_computeShader,  Uniforms._DeformationWeightMaskInfluence, altariaClothEnabled ? math.saturate(deformationWeightMaskInfluence) : 0f);

                int groupsX = GetDispatchGroupsCount(Kernels.kApplyDeformation, (uint)patchVertexCount);
                cmd.DispatchCompute(s_computeShader, Kernels.kApplyDeformation, groupsX, 1, 1);

                cmd.EndSample("Altaria Deformation Apply");
                
                
            }
        }
        
        /**********************************************************
         * Create Resources
         *********************************************************/
        void InitResources()
        {
            if(DebugSentisResource)
                Debug.Log("SentisDeformer InitResources");
            
            CreateSentisResources();
            EnsureMeshInstance();
        }
        void EnsureGPUResources()
        {
            if (s_computeShader == null)
            {
                StaticInitialize();
            }

            if (neutralPositionsBuffer == null)
            {
                CreateGPUDeformationResources();
            }

            if (adjacentTriangleIndicesOffsetCountBuffer == null)
            {
                CreateCommonGPUResources();
            }
        }
        private void CreateCommonGPUResources()
        {
            DestroyCommonGPUResources();

            meshAdjacency = new MeshAdjacency(meshAssetBuffers, false);

            crossProdPerTriangleBuffer = new GraphicsBuffer( GraphicsBuffer.Target.Structured, meshAdjacency.triangleCount, UnsafeUtility.SizeOf<float3>());
            adjacentTriangleIndicesOffsetCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshAdjacency.vertexCount, UnsafeUtility.SizeOf<uint2>());
            adjacentTriangleIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshAdjacency.vertexTriangles.itemCount, sizeof(uint));
            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshAdjacency.triangleCount * 3, sizeof(uint));

            NativeArray<uint> indexOffsetArray = new NativeArray<uint>(meshAdjacency.vertexCount * 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<uint> adjacentTriangleIndicesArray = new NativeArray<uint>(meshAdjacency.vertexTriangles.itemCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int[] indicesArray = meshAssetBuffers.triangles;
            
            //upload data
            uint currentTriangleIndexOffset = 0;
            for (int i = 0; i != meshAdjacency.vertexCount; i++)
            {
                uint triangleOffset = 0;
                foreach (int triangle in meshAdjacency.vertexTriangles[i])
                {
                    adjacentTriangleIndicesArray[(int)(currentTriangleIndexOffset + triangleOffset)] = (uint)triangle;
                    ++triangleOffset;
                }
            
                indexOffsetArray[i * 2] = currentTriangleIndexOffset;
                indexOffsetArray[i * 2 + 1] = triangleOffset;

                currentTriangleIndexOffset += triangleOffset;
            }

            adjacentTriangleIndicesOffsetCountBuffer.SetData(indexOffsetArray);
            adjacentTriangleIndicesBuffer.SetData(adjacentTriangleIndicesArray);
            indexBuffer.SetData(indicesArray);
        
            indexOffsetArray.Dispose();
            adjacentTriangleIndicesArray.Dispose();
        }
        private void CreateGPUDeformationResources()
        {
            DestroyGPUDeformationResources();

            if (_mRenderVertexIds.Length == 0 || _mRenderVertexIds.Length == 0)
            {
                Debug.LogError("No data for _mRenderVertexIds");
                return;
            }
                
            
            neutralPositionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshAssetBuffers.vertexCount, UnsafeUtility.SizeOf<float3>());
            neutralPositionsBuffer.SetData(meshAssetBuffers.vertexPositions);
            
            patchDataToVertexIndexMappingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _mRenderVertexIds.Length, UnsafeUtility.SizeOf<uint>());
            patchDataToVertexIndexMappingBuffer.SetData(_mRenderVertexIds);
        }
        
        private void CreateSentisResources()
        {
            if(DebugSentisResource)
                Debug.Log("Create Sentis Resources");
            
            _mRuntimeModel = ModelLoader.Load(modelAsset);
            var inputs = _mRuntimeModel.inputs;
            _mOutputNames = _mRuntimeModel.outputs;

            _mInputTensors = new Tensor[inputs.Count];

            int totalCount = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                var shapeArray = inputs[i].shape.ToIntArray();
                var shape = TensorShape.Ones(shapeArray.Length);
                shape[-1] = shapeArray[shapeArray.Length - 1];
                tensorData = new Tensor<float>(shape);
                _mInputTensors[i] = tensorData;

                totalCount += tensorData.shape[1];
            }

            if (DebugSentisResource)
            {
                Debug.LogFormat("Total Tensors Input Count:{0}", totalCount);
                Debug.LogFormat("Output Tensors Names:{0}", _mOutputNames);
            }
            
            _mOutputNames = _mOutputNames.OrderBy(o => o.name).ToList();

            _mWorker = _backend switch
            {
                SentisBackend.CPU => new Worker(_mRuntimeModel, BackendType.CPU),
                SentisBackend.GPU => new Worker(_mRuntimeModel, BackendType.GPUCompute),
                _ => new Worker(_mRuntimeModel, BackendType.CPU)
            };
            
            
            if (_vertexPatchData == null)
            {
                Debug.LogError("Sentis Deformer - Could not find vertex data file, please check you have it assigned on the model");
                return;
            }
            _mRenderPatches = _vertexPatchData.GetPatchData();
            int totalVertices = _vertexPatchData.VertexCount;

            if (_mRenderPatches == null)
            {
                Debug.LogWarning("Sentis Deformer - _mRenderPatches was empty");
                return;
            }
                
            if(DebugSentisResource)
                Debug.LogFormat("Sentis Deformer - Read {0} vertex data entries", _mRenderPatches.Count);

            // Create NativeArrays of the patch vertex data
            _mRenderVertexIds = new NativeArray<int>(totalVertices, Allocator.Persistent);
            _mRenderVertexIdOffsets = new NativeArray<int>(_mRenderPatches.Count, Allocator.Persistent);
            _mRenderVertexIdLengths = new NativeArray<int>(_mRenderPatches.Count, Allocator.Persistent);

            int offset = 0;
            int patchOffset = 0;
            for (int i = 0; i < _mRenderPatches.Count; i++)
            {
                _mRenderVertexIdOffsets[i] = patchOffset;
                _mRenderVertexIdLengths[i] = _mRenderPatches[i].Length;
                for (int j = 0; j < _mRenderPatches[i].Length; j++)
                {
                    _mRenderVertexIds[offset] = _mRenderPatches[i][j];
                    offset++;
                }

                patchOffset += _mRenderPatches[i].Length;
            }

            if (DebugSentisResource)
                Debug.LogFormat("Nr of render patches: {0}", _mRenderPatches.Count);

            _resourcesInitialized = true;
        }
        
        /**********************************************************
         * Destroy Resources
         *********************************************************/
        void FreeResources()
        {
#if UNITY_2021_2_OR_NEWER

            DestroyGPUDeformationResources();
            DestroyCommonGPUResources();
#endif
            RemoveMeshInstance();
            FreeSentisResources();
        }
        private void DestroyGPUDeformationResources()
        {
            neutralPositionsBuffer?.Dispose();
            neutralPositionsBuffer = null;
            
            patchDataToVertexIndexMappingBuffer?.Dispose();
            patchDataToVertexIndexMappingBuffer = null;
        }
        
        void DestroyCommonGPUResources()
        {
            crossProdPerTriangleBuffer?.Dispose();
            crossProdPerTriangleBuffer = null;
            
            adjacentTriangleIndicesOffsetCountBuffer?.Dispose();
            adjacentTriangleIndicesOffsetCountBuffer = null;
            
            adjacentTriangleIndicesBuffer?.Dispose();
            adjacentTriangleIndicesBuffer = null;
            
            indexBuffer?.Dispose();
            indexBuffer = null;
        }
        
        private void FreeSentisResources()
        {
            if(DebugSentisResource)
                Debug.Log("Free Sentis Resources");
            
            if(_mRenderVertexIds.IsCreated)
            {
                _mRenderVertexIds.Dispose();
            }
            if(_mRenderVertexIdOffsets.IsCreated)
            {
                _mRenderVertexIdOffsets.Dispose();
            }
            if(_mRenderVertexIdLengths.IsCreated)
            {
                _mRenderVertexIdLengths.Dispose();
            }

            if (_mRenderPatches != null)
            {
                foreach (var patch in _mRenderPatches)
                {
                    patch.Dispose();
                }
            }

            if (_mInputTensors != null)
            {
                foreach (var tensor in _mInputTensors)
                {
                    tensor?.Dispose();
                }
            }

            _mInputTensors = null;
            outputData?.Dispose();
            tensorData?.Dispose();
            outputBuffer?.Dispose();
            _mWorker?.Dispose();
            
            _mRenderPatches = null;
        }
        
        /**********************************************************
         * Misc Functions
         *********************************************************/
        public void ReloadModel()
        {
            InitResources();
            
        }

        public void ConvertVertexListsToBin()
        {
            // var list = new List<List<int>>();

            using (var writer = new BinaryWriter(new FileStream(Application.dataPath + "/Characters/SentisModels/data/patch_data_hero_character.bin", FileMode.Create)))
            {
                foreach (var inner in _mRenderPatches)
                {
                    writer.Write(inner.Length);
                    foreach (var item in inner)
                        writer.Write(item);
                }
            }
            
            
        }

        
        
        void OnDrawGizmos()
        {
        }
    }
}