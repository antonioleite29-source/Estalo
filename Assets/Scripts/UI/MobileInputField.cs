using TMPro;

// Settings every text box in this game needs on a phone.
//
// By default Unity lets Android's own keyboard hold the string and copies it back into the field.
// The two buffers drift apart as soon as anything but plain typing happens, and when they do, one
// backspace replaces the whole field with the keyboard's idea of it — which reads as "deleting one
// character wipes what I typed". Hiding the native input box makes TMP_InputField the only owner of
// the string, so a backspace removes exactly one character.
public static class MobileInputField
{
    public static void MakeSafeForPhones(TMP_InputField field)
    {
        if (field == null)
            return;

        field.shouldHideMobileInput = true;

        // A name or an IP address is one line. Without this an Enter key from the phone keyboard
        // inserts a newline that is invisible in a single-line-looking box but still travels with
        // the value.
        field.lineType = TMP_InputField.LineType.SingleLine;
    }
}
