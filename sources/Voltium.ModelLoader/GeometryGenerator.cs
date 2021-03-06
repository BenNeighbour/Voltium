using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using ObjLoader.Loader.Data;
using ObjLoader.Loader.Data.VertexData;
using ObjLoader.Loader.Loaders;
using Voltium.Common;
using Voltium.ModelLoading;
using Material = Voltium.ModelLoading.Material;
using ObjMaterial = ObjLoader.Loader.Data.Material;
using ObjVertex = ObjLoader.Loader.Data.VertexData.Vertex;
using Vertex = Voltium.ModelLoading.TexturedVertex;

namespace Voltium.Interactive
{
    /// <summary>
    /// A utility class to generate
    /// </summary>
    public static class GeometryGenerator
    {
        /// <summary>
        /// Create a cube with a given radius
        /// </summary>
        /// <param name="radius">The radius of the cube</param>
        /// <returns>A new <see cref="RenderObject{T}"/></returns>
        public static RenderObject<Vertex> CreateCube(float radius)
        {
            var cubeVertices = new Vertex[24]
            {
                // Fill in the front face vertex data.
	            new Vertex(-radius, -radius, -radius, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f),
                new Vertex(-radius, +radius, -radius, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                new Vertex(+radius, +radius, -radius, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                new Vertex(+radius, -radius, -radius, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),

                // Fill in the back face vertex data.
                new Vertex(-radius, -radius, +radius, 0.0f, 0.0f, 1.0f, -1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
                new Vertex(+radius, -radius, +radius, 0.0f, 0.0f, 1.0f, -1.0f, 0.0f, 0.0f, 0.0f, 1.0f),
                new Vertex(+radius, +radius, +radius, 0.0f, 0.0f, 1.0f, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                new Vertex(-radius, +radius, +radius, 0.0f, 0.0f, 1.0f, -1.0f, 0.0f, 0.0f, 1.0f, 0.0f),

                // Fill in the top face vertex data.
                new Vertex(-radius, +radius, -radius, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f),
                new Vertex(-radius, +radius, +radius, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                new Vertex(+radius, +radius, +radius, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                new Vertex(+radius, +radius, -radius, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),

                // Fill in the bottom face vertex data.
                new Vertex(-radius, -radius, -radius, 0.0f, -1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
                new Vertex(+radius, -radius, -radius, 0.0f, -1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 0.0f, 1.0f),
                new Vertex(+radius, -radius, +radius, 0.0f, -1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f),
                new Vertex(-radius, -radius, +radius, 0.0f, -1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 1.0f, 0.0f),

                // Fill in the left face vertex data.
                new Vertex(-radius, -radius, +radius, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, 1.0f),
                new Vertex(-radius, +radius, +radius, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, 0.0f),
                new Vertex(-radius, +radius, -radius, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f),
                new Vertex(-radius, -radius, -radius, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 1.0f, 1.0f),

                // Fill in the right face vertex data.
                new Vertex(+radius, -radius, -radius, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f),
                new Vertex(+radius, +radius, -radius, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f),
                new Vertex(+radius, +radius, +radius, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f),
                new Vertex(+radius, -radius, +radius, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f)
            };

            return new RenderObject<Vertex>(cubeVertices, CubeIndices);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="sliceCount"></param>
        /// <param name="stackCount"></param>
        /// <returns></returns>
        public static RenderObject<Vertex> CreateSphere(float radius, uint sliceCount, uint stackCount)
        {
            var meshData = new ArrayBuilder<Vertex>();

            Vertex topVertex = new(0.0f, +radius, 0.0f, 0.0f, +1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f);
            Vertex bottomVertex = new(0.0f, -radius, 0.0f, 0.0f, -1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);

            meshData.Add(topVertex);

            float phiStep = MathF.PI / stackCount;
            float thetaStep = 2.0f * MathF.PI / sliceCount;

            // Compute vertices for each stack ring (do not count the poles as rings).
            for (var i = 1; i <= stackCount - 1; ++i)
            {
                float phi = i * phiStep;

                // Vertices of ring.
                for (var j = 0; j <= sliceCount; ++j)
                {
                    float theta = j * thetaStep;

                    Vertex v;

                    // spherical to cartesian
                    v.Position.X = radius * MathF.Sin(phi) * MathF.Cos(theta);
                    v.Position.Y = radius * MathF.Cos(phi);
                    v.Position.Z = radius * MathF.Sin(phi) * MathF.Sin(theta);

                    // Partial derivative of P with respect to theta
                    v.Tangent.X = -radius * MathF.Sin(phi) * MathF.Sin(theta);
                    v.Tangent.Y = 0.0f;
                    v.Tangent.Z = +radius * MathF.Sin(phi) * MathF.Cos(theta);

                    v.Tangent = Vector3.Normalize(v.Tangent);
                    v.Normal = Vector3.Normalize(v.Position);

                    v.TexC.X = theta / (MathF.PI * 2);
                    v.TexC.Y = phi / MathF.PI;

                    meshData.Add(v);
                }
            }

            meshData.Add(bottomVertex);

            //
            // Compute indices for top stack.  The top stack was written first to the vertex buffer
            // and connects the top pole to the first ring.
            //


            var indices = new ArrayBuilder<uint>();

            for (uint i = 1; i <= sliceCount; ++i)
            {
                indices.Add(0);
                indices.Add(i + 1);
                indices.Add(i);
            }

            //
            // Compute indices for inner stacks (not connected to poles).
            //

            // Offset the indices to the index of the first vertex in the first ring.
            // This is just skipping the top pole vertex.
            uint baseIndex = 1;
            uint ringVertexCount = sliceCount + 1;
            for (uint i = 0; i < stackCount - 2; ++i)
            {
                for (uint j = 0; j < sliceCount; ++j)
                {
                    indices.Add(baseIndex + (i * ringVertexCount) + j);
                    indices.Add(baseIndex + (i * ringVertexCount) + j + 1);
                    indices.Add(baseIndex + ((i + 1) * ringVertexCount) + j);

                    indices.Add(baseIndex + ((i + 1) * ringVertexCount) + j);
                    indices.Add(baseIndex + (i * ringVertexCount) + j + 1);
                    indices.Add(baseIndex + ((i + 1) * ringVertexCount) + j + 1);
                }
            }

            //
            // Compute indices for bottom stack.  The bottom stack was written last to the vertex buffer
            // and connects the bottom pole to the bottom ring.
            //

            // South pole vertex was added last.
            uint southPoleIndex = (uint)meshData.Length - 1;

            // Offset the indices to the index of the first vertex in the last ring.
            baseIndex = southPoleIndex - ringVertexCount;

            for (uint i = 0; i < sliceCount; ++i)
            {
                indices.Add(southPoleIndex);
                indices.Add(baseIndex + i);
                indices.Add(baseIndex + i + 1);
            }

            return new RenderObject<Vertex>(meshData.MoveTo(), indices.MoveTo());
        }

        private const string AssetsFolder = "Assets/";

        private sealed class AssetsProvider : IMaterialStreamProvider
        {
            public Stream Open(string materialFilePath) => File.OpenRead(AssetsFolder + materialFilePath);
        }

        private static readonly ObjLoaderFactory _factory = new();
        private static readonly IMaterialStreamProvider _assetsProvider = new AssetsProvider();
        private static ThreadLocal<IObjLoader> _loader = new(() => { lock (_factory) { return _factory.Create(_assetsProvider); } });

        /// <summary>
        /// Load a OBJ file
        /// </summary>
        private static RenderObject<Vertex> LoadSingleModel(string filename, in Material material = default)
        {
            var model = _loader.Value!.Load(File.OpenRead(AssetsFolder + filename));

            var indexCount = model.Groups
                .Aggregate(0, (val, group) => val += group.Faces
                    .Aggregate(0, (val, face) => val + face.Count));

            var indices = new uint[indexCount];
            var vertices = new Vertex[indexCount];


            int c = 0;
            for (int i = 0; i < model.Groups.Count; i++)
            {
                var group = model.Groups[i];
                for (var j = 0; j < group.Faces.Count; j++)
                {
                    var face = group.Faces[j];
                    for (var k = 0; k < face.Count; k++)
                    {
                        Debug.Assert(face.Count == 3);
                        var vertex = face[k];

                        var position = ToVector3(model.Vertices[vertex.VertexIndex - 1]);
                        var normal = ToVector3(model.Normals[vertex.NormalIndex - 1]);
                        var tex = ToVector2(model.Textures[vertex.TextureIndex - 1]);

                        //vertices[c] = new Vertex(position, normal, tex);
                        indices[c] = (ushort)c;

                        c++;
                    }
                }
            }

            return new RenderObject<Vertex>(vertices, indices, material);
        }

        private static Vector3 ToVector3(ObjVertex vertex) => new Vector3(vertex.X, vertex.Y, vertex.Z);
        private static Vector3 ToVector3(Normal normal) => new Vector3(normal.X, normal.Y, normal.Z);
        private static Vector3 ToVector3(Vec3 vec) => new Vector3(vec.X, vec.Y, vec.Z);
        private static Vector2 ToVector2(Texture tex) => new Vector2(tex.X, tex.Y);

        private static Material ToMaterial(ObjMaterial material) => new Material
        {
            DiffuseAlbedo = new Vector4(ToVector3(material.DiffuseColor), 1),
            ReflectionFactor = new Vector4(ToVector3(material.SpecularColor), 1),
            Shininess = material.SpecularCoefficient
        };

        private static uint[] CubeIndices = new uint[36]
        {
            // Fill in the front face index data
	        0, 1, 2,
            0, 2, 3,

	        // Fill in the back face index data
	        4, 5, 6,
            4, 6, 7,

	        // Fill in the top face index data
	        8, 9, 10,
            8, 10, 11,

	        // Fill in the bottom face index data
	        12, 13, 14,
            12, 14, 15,

	        // Fill in the left face index data
	        16, 17, 18,
            16, 18, 19,

	        // Fill in the right face index data
	        20, 21, 22,
            20, 22, 23
        };
    }
}
