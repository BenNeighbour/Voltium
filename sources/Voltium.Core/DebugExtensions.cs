using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using TerraFX.Interop;

namespace Voltium.Common
{
    internal unsafe static class DebugExtensions
    {
//        [DebuggerNonUserCode]
//        public static void PossibleDeviceDisconnect(this ComputeDevice device)
//            => PossibleDeviceDisconnect(device.Get());

//        [DebuggerNonUserCode]
//        public static void PossibleDeviceDisconnect(ID3D12Device* device)
//        {
//#if !DEBUG
//            throw new Exception("This should never even be present in release builds. Use only for specific debug issues");
//#else
//            var reason = device->GetDeviceRemovedReason();
//            if (reason == Windows.S_OK)
//            {
//                return;
//            }

//            /* inspect this */
//            throw new DeviceDisconnectedException("Device disconnected unexpectedly", reason);
//#endif
//        }

        public static bool IsDeviceRemoved(this UniqueComPtr<ID3D12Device> device) =>
            device.Ptr->GetDeviceRemovedReason() != Windows.S_OK;

        public static string DeviceRemovedMessage(int removedReason) => TranslateHr(removedReason);

        public static string TranslateHr(int hr)
        {
#if REFLECTION
            var type = typeof(Windows);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is int value && value == hr)
                {
                    return field.Name;
                }
            }
#endif

            return "<unmapped>";
        }
    }
}
