using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;

namespace Mocks
{
    internal static class ConditionalResultActivator
    {
        public static ConditionalResult<T> Create<T>(bool result, T value)
        {
            return (ConditionalResult<T>)Activator.CreateInstance(
                    typeof(ConditionalResult<T>),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new object[] { result, value },
                    null);
        }
    }
}
