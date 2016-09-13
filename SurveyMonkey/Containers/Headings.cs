﻿using Newtonsoft.Json;

namespace SurveyMonkey.Containers
{
    [JsonConverter(typeof(LaxPropertyNameJsonConverter))]
    public class Headings
    {
        public string Heading { get; set; }
        public string Description { get; set; }
        public Image Image { get; set; }
    }
}