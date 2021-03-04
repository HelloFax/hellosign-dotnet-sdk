using Newtonsoft.Json;

namespace HelloSign
{
    [JsonObject(MemberSerialization.OptIn)]
    public class WhiteLabel
    {
        [JsonProperty("page_background_color", NullValueHandling = NullValueHandling.Ignore)]
        public string PageBackgroundColor { get; set; }

        [JsonProperty("header_background_color", NullValueHandling = NullValueHandling.Ignore)]
        public string HeaderBackgroundColor { get; set; }

        [JsonProperty("text_color1", NullValueHandling = NullValueHandling.Ignore)]
        public string TextColor1 { get; set; }

        [JsonProperty("text_color2", NullValueHandling = NullValueHandling.Ignore)]
        public string TextColor2 { get; set; }

        [JsonProperty("link_color", NullValueHandling = NullValueHandling.Ignore)]
        public string LinkColor { get; set; }

        [JsonProperty("primary_button_color", NullValueHandling = NullValueHandling.Ignore)]
        public string PrimaryButtonColor { get; set; }

        [JsonProperty("primary_button_text_color", NullValueHandling = NullValueHandling.Ignore)]
        public string PrimaryButtonTextColor { get; set; }

        [JsonProperty("primary_button_color_hover", NullValueHandling = NullValueHandling.Ignore)]
        public string PrimaryButtonColorHover { get; set; }

        [JsonProperty("primary_button_text_color_hover", NullValueHandling = NullValueHandling.Ignore)]
        public string PrimaryButtonTextColorHover { get; set; }

        [JsonProperty("secondary_button_color", NullValueHandling = NullValueHandling.Ignore)]
        public string SecondaryButtonColor { get; set; }

        [JsonProperty("secondary_button_text_color", NullValueHandling = NullValueHandling.Ignore)]
        public string SecondaryButtonTextColor { get; set; }

        [JsonProperty("secondary_button_color_hover", NullValueHandling = NullValueHandling.Ignore)]
        public string SecondaryButtonColorHover { get; set; }

        [JsonProperty("secondary_button_text_color_hover", NullValueHandling = NullValueHandling.Ignore)]
        public string SecondaryButtonTextColorHover { get; set; }
    }
}
