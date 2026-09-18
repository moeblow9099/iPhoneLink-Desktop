using System.Text;

namespace PhoneLinkDiag.Services;

public static class BMessageBuilder
{
    public static string BuildSms(string phoneNumber, string message)
    {
        phoneNumber = phoneNumber.Trim();
        message = message.Replace("\r\n", "\n").Trim();
        var msgBytes = Encoding.UTF8.GetByteCount(message);

        return "BEGIN:BMSG\r\n" +
               "VERSION:1.0\r\n" +
               "STATUS:UNREAD\r\n" +
               "TYPE:SMS_GSM\r\n" +
               "FOLDER:telecom/msg/outbox\r\n" +
               "BEGIN:VCARD\r\n" +
               "VERSION:2.1\r\n" +
               "N:\r\n" +
               $"TEL:{phoneNumber}\r\n" +
               "END:VCARD\r\n" +
               "BEGIN:BENV\r\n" +
               "BEGIN:VCARD\r\n" +
               "VERSION:2.1\r\n" +
               "N:\r\n" +
               $"TEL:{phoneNumber}\r\n" +
               "END:VCARD\r\n" +
               "BEGIN:BBODY\r\n" +
               "CHARSET:UTF-8\r\n" +
               $"LENGTH:{msgBytes}\r\n" +
               "BEGIN:MSG\r\n" +
               message + "\r\n" +
               "END:MSG\r\n" +
               "END:BBODY\r\n" +
               "END:BENV\r\n" +
               "END:BMSG\r\n";
    }
}
