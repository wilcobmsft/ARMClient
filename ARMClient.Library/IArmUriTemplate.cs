using System;

namespace ARMClient.Library
{
    public interface IArmUriTemplate
    {
        Uri Bind(object obj);
    }
}
