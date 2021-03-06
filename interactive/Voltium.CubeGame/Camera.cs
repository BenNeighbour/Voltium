using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voltium.CubeGame
{
    public class Camera
    {
        public Camera()
        {
            View = Matrix4x4.CreateLookAt(
                new Vector3(0.0f, 0.7f, 1.5f),
                new Vector3(0.0f, -0.1f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f)
            );
            //View = Matrix4x4.CreateLookAt(new Vector3(0, 0, -1), new Vector3(0, 0, 0), new Vector3(0, 1, 0));
            const float defaultFov = 70.0f * (float)Math.PI / 180.0f;
            Projection = Matrix4x4.CreatePerspectiveFieldOfView(defaultFov, 1, 0.01f, 100f);
        }

        public Matrix4x4 View { get; private set; }
        public Matrix4x4 Projection { get; private set; }

        public void TranslateX(float x) => Translate(x, 0, 0);
        public void TranslateY(float y) => Translate(0, y, 0);
        public void TranslateZ(float z) => Translate(0, 0, z);

        public void Translate(float x, float y, float z) => Translate(new Vector3(x, y, z));

        public void Translate(Vector3 xyz)
        {
            View *= Matrix4x4.CreateTranslation(xyz);
        }

        public void RotateX(float radians) => View *= Matrix4x4.CreateRotationX(radians);
        public void RotateY(float radians) => View *= Matrix4x4.CreateRotationY(radians);
        public void RotateZ(float radians) => View *= Matrix4x4.CreateRotationZ(radians);
    }
}
