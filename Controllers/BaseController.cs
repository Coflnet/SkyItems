using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.Items.Services;

namespace Coflnet.Sky.Items.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ItemsController : ControllerBase
    {
        private readonly ItemService service;

        /// <summary>
        /// Creates a new instance of <see cref="ItemsController"/>
        /// </summary>
        /// <param name="service"></param>
        public ItemsController(ItemService service)
        {
            this.service = service;
        }

        /// <summary>
        /// Tracks a flip
        /// </summary>
        /// <param name="flip"></param>
        /// <param name="auction"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("flip/{AuctionId}")]
        public async Task AddDetailsForAuction([FromBody] hypixel.SaveAuction auction)
        {
        }
    }
}
