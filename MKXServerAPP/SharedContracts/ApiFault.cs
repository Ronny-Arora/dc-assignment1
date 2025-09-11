using System;

namespace SharedContracts
{
	[DataContract]
	public class ApiFault
	{
		[DataMember(Order = 1)] public string Code { get; set; } = "BadRequest";
		[DataMember(Order = 1)] public string Message { get; set; } = "";
        [DataMember(Order = 1)] public string? Details { get; set; }
    }
}
