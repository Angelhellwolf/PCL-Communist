Public Class PageLinkMain
    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared ReadOnly LinkState As LinkStates = LinkStates.Waiting
    Public Shared Sub Join()
        Hint("此版本已移除联机功能。")
    End Sub
    Public Shared Sub Join(Code As String)
        Join()
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        Return "此版本已移除联机功能。"
    End Function
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        Return False
    End Function
    Public Shared Sub ProcessStop()
    End Sub
End Class
