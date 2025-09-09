using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
    [CreateAssetMenu(fileName = "Data", menuName = "Altaria/PatchVertexData", order = 1)]
    public class PatchVertexDataScriptableObject : ScriptableObject
    {
        //public string vertexDataPath;
        public string assetPath;
        [SerializeField] private int _totalVertices;

        [SerializeField] private int _totalPatchCount;
        
        //[SerializeField] private List<List<int>> _mRenderPatches;
        [HideInInspector] [SerializeField] private List<string> m_RenderPatchDataString;
        
        public List<NativeArray<int>> GetPatchData()
        {
            if (m_RenderPatchDataString == null || m_RenderPatchDataString.Count == 0)
                return null;
            
            
            List<NativeArray<int>> patchData = new List<NativeArray<int>>();
            for (int i = 0; i < m_RenderPatchDataString.Count; i++)
            {
                string[] data = m_RenderPatchDataString[i].Split(",");
                
                NativeArray<int> patch = new NativeArray<int>(data.Length, Allocator.Persistent);
                for (int j = 0; j < data.Length; j++)
                {
                    patch[j] = int.Parse(data[j]);
                }
                patchData.Add(patch);
            }
            
            return patchData;
            
            /*
            List<NativeArray<int>> patchData = new List<NativeArray<int>>();
            for (int i = 0; i < _mRenderPatches.Count; i++)
            {
                NativeArray<int> patch = new NativeArray<int>(_mRenderPatches[i].Count, Allocator.Persistent);
                for (int j = 0; j < _mRenderPatches[i].Count; j++)
                {
                    patch[j] = _mRenderPatches[i][j];
                }
                patchData.Add(patch);
            }
            
            return patchData;
            */
        }
        
        /*
        public void SetPatchData(List<List<int>> data)
        {
            _mRenderPatches = data;
            
        }
        */

        public void SetVertexStringData(List<string> data)
        {
            m_RenderPatchDataString = data;
        }
        
        public int VertexCount
        {
            get { return _totalVertices; }
            set { _totalVertices = value; }
        }
        
    }
}
