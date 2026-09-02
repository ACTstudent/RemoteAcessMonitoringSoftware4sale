namespace Server.Authorization;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class TeacherSharedActionAttribute : Attribute;
